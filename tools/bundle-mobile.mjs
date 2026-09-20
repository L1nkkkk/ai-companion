import { mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { spawnSync } from 'node:child_process';

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const output = join(root, '.tmp', 'mobile-bundles');
mkdirSync(output, { recursive: true });

for (const platform of ['android', 'ios']) {
  const result = spawnSync(process.execPath, [
    join(root, 'node_modules', 'react-native', 'cli.js'), 'bundle',
    '--platform', platform, '--dev', 'false', '--entry-file', 'index.js',
    '--bundle-output', join(output, `${platform}.bundle`),
    '--assets-dest', join(output, platform), '--max-workers', '2',
  ], { cwd: join(root, 'apps', 'mobile'), stdio: 'inherit' });
  if (result.error) throw result.error;
  if (result.status !== 0) process.exit(result.status ?? 1);
}
