import importlib.util
import sys
from pathlib import Path

package = Path(sys.argv[1]).resolve()
evidence = Path(sys.argv[2]).resolve()
evidence.mkdir(parents=True, exist_ok=True)
spec = importlib.util.spec_from_file_location('portable_launcher', package / 'launch_desktop.py')
launcher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(launcher)
command = [str(package / 'NeuroSaki.exe'), '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '800',
           '-userDataPath', str(evidence / 'userdata'), '-evidenceDirectory', str(evidence),
           '-desktopTest', 'fixture', '-smokeSeconds', '600', '-logFile', str(evidence / 'Player.log')]
if len(sys.argv) > 3 and sys.argv[3] == 'close':
    command += ['-quitDuringPlayback', 'true']
raise SystemExit(launcher.run_desktop(package, evidence / 'runtime', player_command=command))
