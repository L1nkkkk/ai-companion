type PlayOptions = {
  signal: AbortSignal;
  volume: number;
  speed: number;
  onStart: () => void;
  onLevel: (level: number) => void;
};

export class VoicePlayer {
  private context: AudioContext | null = null;
  private cancelCurrent: (() => void) | null = null;

  unlock(): void {
    if (!this.context || this.context.state === "closed")
      this.context = new AudioContext();
    void this.context.resume().catch(() => undefined);
  }

  stop(): void {
    this.cancelCurrent?.();
    this.cancelCurrent = null;
    window.speechSynthesis?.cancel();
  }

  dispose(): void {
    this.stop();
    void this.context?.close().catch(() => undefined);
    this.context = null;
  }

  async playWave(bytes: ArrayBuffer, options: PlayOptions): Promise<void> {
    this.unlock();
    const context = this.context!;
    const buffer = await context.decodeAudioData(bytes);
    if (options.signal.aborted) throw new DOMException("Aborted", "AbortError");
    await context.resume();
    if (options.signal.aborted) throw new DOMException("Aborted", "AbortError");
    await new Promise<void>((resolve) => {
      const source = context.createBufferSource();
      const gain = context.createGain();
      const analyser = context.createAnalyser();
      source.buffer = buffer;
      source.playbackRate.value = options.speed;
      gain.gain.value = options.volume;
      analyser.fftSize = 512;
      source.connect(gain).connect(analyser).connect(context.destination);
      const samples = new Float32Array(analyser.fftSize);
      let frame = 0;
      let finished = false;
      let lastMeterTime = 0;
      const finish = () => {
        if (finished) return;
        finished = true;
        cancelAnimationFrame(frame);
        options.signal.removeEventListener("abort", cancel);
        source.disconnect();
        gain.disconnect();
        analyser.disconnect();
        options.onLevel(0);
        this.cancelCurrent = null;
        resolve();
      };
      const cancel = () => {
        try {
          source.stop();
        } catch {
          /* Already ended. */
        }
        finish();
      };
      this.cancelCurrent = cancel;
      options.signal.addEventListener("abort", cancel, { once: true });
      source.onended = finish;
      const meter = (time: number) => {
        if (finished) return;
        if (time - lastMeterTime > 45) {
          analyser.getFloatTimeDomainData(samples);
          const rms = Math.sqrt(
            samples.reduce((total, value) => total + value * value, 0) /
              samples.length,
          );
          options.onLevel(Math.min(1, rms * 5));
          lastMeterTime = time;
        }
        frame = requestAnimationFrame(meter);
      };
      source.start();
      options.onStart();
      frame = requestAnimationFrame(meter);
    });
  }

  async playBrowser(
    text: string,
    voice: string,
    options: PlayOptions,
  ): Promise<void> {
    if (!("speechSynthesis" in window))
      throw new Error("这个浏览器没有可用的语音播放功能。");
    if (options.signal.aborted) throw new DOMException("Aborted", "AbortError");
    await new Promise<void>((resolve, reject) => {
      const utterance = new SpeechSynthesisUtterance(text);
      utterance.lang = "zh-CN";
      utterance.rate = options.speed;
      utterance.volume = options.volume;
      utterance.voice =
        window.speechSynthesis
          .getVoices()
          .find((item) => item.name === voice) ??
        window.speechSynthesis
          .getVoices()
          .find((item) => item.lang.startsWith("zh")) ??
        null;
      let finished = false;
      let watchdog = 0;
      const finish = (error?: Error) => {
        if (finished) return;
        finished = true;
        window.clearTimeout(watchdog);
        options.signal.removeEventListener("abort", cancel);
        this.cancelCurrent = null;
        options.onLevel(0);
        error ? reject(error) : resolve();
      };
      const cancel = () => {
        window.speechSynthesis.cancel();
        finish();
      };
      this.cancelCurrent = cancel;
      options.signal.addEventListener("abort", cancel, { once: true });
      const timeout = () => {
        finish(new Error("浏览器语音没有响应，请更换音色或关闭自动朗读。"));
        window.speechSynthesis.cancel();
      };
      watchdog = window.setTimeout(timeout, 10000);
      utterance.onstart = () => {
        if (finished || options.signal.aborted) return;
        window.clearTimeout(watchdog);
        watchdog = window.setTimeout(timeout, 180000);
        options.onStart();
      };
      utterance.onend = () => finish();
      utterance.onerror = (event) =>
        finish(
          options.signal.aborted ||
            event.error === "canceled" ||
            event.error === "interrupted"
            ? undefined
            : new Error("浏览器未能播放语音，可尝试更换音色。"),
        );
      window.speechSynthesis.speak(utterance);
    });
  }
}
