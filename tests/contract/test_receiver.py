import copy
from uuid import UUID, uuid4

import pytest
from mocks.protocol import AudioFrameHeader, encode_frame
from mocks.receiver import PcmReceiver

SESSION = "11111111-1111-4111-8111-111111111111"
TURN = "33333333-3333-4333-8333-333333333333"
STREAM = "44444444-4444-4444-8444-444444444444"
DEVICE = "22222222-2222-4222-8222-222222222222"


def wire(*, seq=0, offset=0, samples=2400, stream=STREAM, turn=TURN, epoch=1, kind=2, segment=0):
    h = AudioFrameHeader(
        kind,
        epoch,
        seq,
        24000 if kind == 2 else 16000,
        stream,
        turn if kind == 2 else None,
        segment,
        offset,
    )
    return encode_frame(h, b"\x01\x00" * samples)


def output(events, clock):
    receiver = PcmReceiver(SESSION, clock=clock)
    assert receiver.receive_control(events("turn.started")) == "turn_started"
    assert receiver.receive_control(events("assistant.segment")) == "segment_registered"
    return receiver


def input_receiver(clock):
    receiver = PcmReceiver(SESSION, kind=1, input_device_id=DEVICE, clock=clock)
    assert receiver.accept_input(STREAM, device_id=DEVICE, session_epoch=1) == "accepted"
    return receiver


def test_audio_arrives_at_100ms_control_at_300ms_and_never_plays_unmatched(events, clock):
    receiver = PcmReceiver(SESSION, clock=clock)
    clock.advance(0.1)
    assert receiver.receive_audio(wire()) == "waiting_metadata"
    assert receiver.take_ready() == []
    clock.advance(0.2)
    receiver.receive_control(events("turn.started"))
    assert receiver.receive_control(events("assistant.segment")) == "segment_registered"
    frames = receiver.take_ready()
    assert len(frames) == 1
    assert frames[0].header.stream_id == UUID(STREAM)
    assert frames[0].sample_count == 2400
    assert receiver.unknown_bytes == 0


def test_unknown_stream_500ms_timeout_is_host_driven_even_without_more_messages(events, clock):
    receiver = PcmReceiver(SESSION, clock=clock)
    receiver.receive_audio(wire())
    clock.advance(0.5)
    receiver.tick()
    assert receiver.resync_requested and receiver.unknown_bytes == 0
    assert receiver.errors[-1]["code"] == "unknown_stream_timeout"
    assert receiver.receive_control(events("turn.started")) == "resync_required"
    assert receiver.take_ready() == []


def test_metadata_before_deadline_releases_audio(events, clock):
    receiver = PcmReceiver(SESSION, clock=clock)
    receiver.receive_audio(wire())
    clock.advance(0.499)
    receiver.receive_control(events("turn.started"))
    receiver.receive_control(events("assistant.segment"))
    assert len(receiver.take_ready()) == 1


def test_unknown_64kib_limit_is_global_and_counts_headers(clock):
    receiver = PcmReceiver(SESSION, clock=clock)
    for i in range(6):
        assert (
            receiver.receive_audio(wire(stream=str(UUID(int=i + 1)), samples=4800))
            == "waiting_metadata"
        )
    assert receiver.unknown_bytes == 6 * (64 + 9600)
    assert receiver.receive_audio(wire(stream=str(uuid4()), samples=4800)) == "unknown_buffer_limit"
    for _ in range(100):
        assert receiver.receive_audio(wire(stream=str(uuid4()), samples=4800)) == "resync_required"
    assert receiver.unknown_bytes <= 65536
    clock.advance(0.5)
    receiver.tick()
    assert receiver.unknown_bytes == 0 and not receiver.ready


@pytest.mark.parametrize(
    "stage",
    ["text_generation", "tts_before_playback", "during_queue_drain", "audio_before_metadata"],
)
def test_local_cancel_drops_late_audio_text_and_avatar_without_waiting_ack(events, clock, stage):
    receiver = PcmReceiver(SESSION, clock=clock)
    receiver.receive_control(events("turn.started"))
    receiver.receive_control(events("assistant.text.delta"))
    if stage == "audio_before_metadata":
        receiver.receive_audio(wire())
    elif stage != "text_generation":
        receiver.receive_control(events("assistant.segment"))
        receiver.receive_audio(wire())
        if stage == "during_queue_drain":
            assert receiver.take_ready()
            receiver.receive_audio(wire(seq=1, offset=2400))
    receiver.cancel(TURN)
    assert receiver.unknown_bytes == receiver.queued_bytes == 0
    assert not receiver.text_deltas and not receiver.avatar_states
    assert receiver.receive_audio(wire(seq=2, offset=4800)) == "terminal_turn"
    assert receiver.receive_control(events("assistant.text.delta")) == "terminal_turn"
    assert receiver.receive_control(events("avatar.state")) == "terminal_turn"
    assert (
        receiver.receive_control(events("assistant.segment", stream_id=str(uuid4())))
        == "terminal_turn"
    )
    assert receiver.take_ready() == []
    receiver.cancel(TURN)
    assert TURN in receiver.cancelled_turns


def test_epoch_takeover_clears_every_queue_and_old_control_audio(events, clock):
    receiver = output(events, clock)
    receiver.receive_audio(wire())
    receiver.cancel(TURN)
    snapshot = events("session.snapshot", epoch=2, active_turn_id=None)
    assert receiver.receive_control(snapshot) == "snapshot"
    assert receiver.epoch == 2 and not receiver.cancelled_turns
    assert receiver.receive_audio(wire()) == "stale_epoch"
    assert receiver.receive_control(events("assistant.text.delta")) == "stale_epoch"
    assert receiver.take_ready() == []


@pytest.mark.parametrize(
    "second",
    [
        {"seq": 0, "offset": 2400},
        {"seq": 2, "offset": 2400},
        {"seq": 1, "offset": 2399},
        {"seq": 1, "offset": 2401},
    ],
)
def test_sequence_duplicates_gaps_and_offsets_stop_stream_without_partial_replay(
    events, clock, second
):
    receiver = output(events, clock)
    assert receiver.receive_audio(wire()) == "accepted"
    assert receiver.receive_audio(wire(**second)) == "stream_discontinuity"
    assert receiver.take_ready() == []
    assert receiver.resync_requested


@pytest.mark.parametrize("patch", [{"turn": str(UUID(int=91))}, {"segment": 2}])
def test_header_must_match_segment_identity(events, clock, patch):
    receiver = output(events, clock)
    assert receiver.receive_audio(wire(**patch)) == "metadata_mismatch"
    assert not receiver.ready


def test_output_end_can_precede_final_binary_frame_by_less_than_500ms(events, clock):
    receiver = output(events, clock)
    receiver.receive_audio(wire())
    assert (
        receiver.receive_control(events("assistant.audio.end", total_samples=4800))
        == "waiting_end_frames"
    )
    clock.advance(0.499)
    assert receiver.receive_audio(wire(seq=1, offset=2400)) == "accepted"
    assert receiver.streams[STREAM].complete
    assert sum(f.sample_count for f in receiver.take_ready()) == 4800


def test_end_timeout_and_extra_frames_do_not_silently_complete(events, clock):
    receiver = output(events, clock)
    receiver.receive_audio(wire())
    receiver.receive_control(events("assistant.audio.end", total_samples=4800))
    clock.advance(0.5)
    receiver.tick()
    assert receiver.errors[-1]["code"] == "incomplete_stream"
    assert receiver.take_ready() == []


def test_output_end_total_conflicts_and_data_after_end(events, clock):
    receiver = output(events, clock)
    receiver.receive_audio(wire())
    assert receiver.end_stream(STREAM, 2400) == "complete"
    assert receiver.end_stream(STREAM, 2400) == "duplicate_end"
    assert receiver.receive_audio(wire(seq=1, offset=2400)) == "frame_after_end"
    assert receiver.take_ready() == []


def test_smaller_declared_total_is_rejected(events, clock):
    receiver = output(events, clock)
    receiver.receive_audio(wire())
    assert receiver.end_stream(STREAM, 2399) == "end_total_mismatch"


def test_playout_buffer_never_exceeds_two_seconds_or_skips_middle_audio(events, clock):
    receiver = output(events, clock)
    for i in range(20):
        assert receiver.receive_audio(wire(seq=i, offset=i * 2400)) == "accepted"
    assert receiver.queued_bytes == 96000
    assert receiver.receive_audio(wire(seq=20, offset=48000)) == "playout_backpressure"
    assert receiver.queued_bytes == 0
    assert receiver.resync_requested


def test_simulated_sink_drain_allows_contiguous_further_frames(events, clock):
    receiver = output(events, clock)
    for i in range(40):
        assert receiver.receive_audio(wire(seq=i, offset=i * 2400)) == "accepted"
        if i % 10 == 9:
            assert len(receiver.take_ready()) == 10
    assert receiver.streams[STREAM].total_samples == 96000


def test_input_requires_device_epoch_and_accepted_stream(clock):
    receiver = PcmReceiver(SESSION, kind=1, input_device_id=DEVICE, clock=clock)
    frame = wire(kind=1, samples=1600)
    assert receiver.receive_audio(frame, device_id=DEVICE) == "input_not_accepted"
    assert (
        receiver.accept_input(STREAM, device_id=str(uuid4()), session_epoch=1)
        == "input_not_authorized"
    )
    assert (
        receiver.accept_input(STREAM, device_id=DEVICE, session_epoch=0) == "input_not_authorized"
    )
    assert receiver.accept_input(STREAM, device_id=DEVICE, session_epoch=1) == "accepted"
    assert receiver.receive_audio(frame, device_id=str(uuid4())) == "input_not_authorized"
    assert receiver.receive_audio(frame, device_id=DEVICE) == "accepted"


def test_input_longer_than_30_seconds_is_rejected(clock):
    receiver = input_receiver(clock)
    for i in range(300):
        assert (
            receiver.receive_audio(
                wire(kind=1, seq=i, offset=i * 1600, samples=1600), device_id=DEVICE
            )
            == "accepted"
        )
    assert (
        receiver.receive_audio(wire(kind=1, seq=300, offset=480000, samples=1), device_id=DEVICE)
        == "input_duration_limit"
    )
    assert receiver.queued_bytes == 0


def test_input_end_declares_exact_sequence_and_total_and_handles_empty(clock):
    receiver = input_receiver(clock)
    assert receiver.end_stream(STREAM, 0, last_frame_seq=None) == "complete"
    next_stream = str(uuid4())
    assert receiver.accept_input(next_stream, device_id=DEVICE, session_epoch=1) == "accepted"
    assert receiver.end_stream(next_stream, 1600, last_frame_seq=0) == "waiting_end_frames"
    assert (
        receiver.receive_audio(wire(kind=1, samples=1600, stream=next_stream), device_id=DEVICE)
        == "accepted"
    )
    assert receiver.streams[next_stream].complete


@pytest.mark.parametrize("total,last", [(0, 0), (1, None), (1600, 1)])
def test_input_end_inconsistent_empty_or_last_sequence_rejected(clock, total, last):
    receiver = input_receiver(clock)
    if total == 1600:
        receiver.receive_audio(wire(kind=1, samples=1600), device_id=DEVICE)
    assert receiver.end_stream(STREAM, total, last_frame_seq=last) == "end_sequence_mismatch"


def test_controls_deduplicate_event_and_reject_sequence_regression(events, clock):
    receiver = PcmReceiver(SESSION, clock=clock)
    event = events("turn.started")
    assert receiver.receive_control(event) == "turn_started"
    assert receiver.receive_control(event) == "duplicate_event"
    older = copy.deepcopy(event)
    older["event_id"] = str(uuid4())
    assert receiver.receive_control(older) == "stale_control_seq"
    assert (
        receiver.receive_control(events("turn.started", turn=str(uuid4())))
        == "active_turn_conflict"
    )


def test_foreign_session_controls_cannot_open_audio_metadata(events, clock):
    receiver = PcmReceiver(SESSION, clock=clock)
    event = events("turn.started")
    event["session_id"] = str(uuid4())
    assert receiver.receive_control(event) == "wrong_session"
    assert receiver.active_turn is None


def test_authoritative_same_epoch_snapshot_retires_old_turn_and_all_pending_work(events, clock):
    receiver = output(events, clock)
    receiver.receive_audio(wire())
    receiver.receive_control(events("assistant.text.delta"))
    receiver.receive_control(events("avatar.state"))
    assert receiver.receive_control(events("session.snapshot", active_turn_id=None)) == "snapshot"
    assert receiver.active_turn is None
    assert receiver.receive_audio(wire(seq=1, offset=2400)) == "terminal_turn"
    assert receiver.take_ready() == []
    assert not receiver.text_deltas and not receiver.avatar_states
    assert not receiver.streams and not receiver.unknown


def test_same_epoch_resync_snapshot_recovers_without_replaying_failed_stream(events, clock):
    receiver = PcmReceiver(SESSION, clock=clock)
    receiver.receive_audio(wire())
    clock.advance(0.5)
    receiver.tick()
    assert receiver.resync_requested
    assert receiver.receive_control(events("session.snapshot", active_turn_id=None)) == "snapshot"
    assert not receiver.resync_requested and not receiver.ready
    assert receiver.receive_audio(wire()) == "stopped_stream"
    receiver.receive_control(events("turn.started"))
    new_stream = str(uuid4())
    assert (
        receiver.receive_control(events("assistant.segment", stream_id=new_stream))
        == "segment_registered"
    )
    assert receiver.receive_audio(wire(stream=new_stream)) == "accepted"
