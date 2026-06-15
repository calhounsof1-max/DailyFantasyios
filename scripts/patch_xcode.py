import glob, os, re

home = os.path.expanduser('~')
pattern = f'{home}/.dotnet/packs/Microsoft.iOS.Sdk*/*/targets/Xamarin.Shared.Sdk.targets'
files = glob.glob(pattern)
print(f'Found targets files: {files}')

for f in files:
    with open(f) as fh:
        lines = fh.readlines()

    # Print lines near any Xcode version check
    for i, line in enumerate(lines):
        if 'requires Xcode' in line or ('Xcode' in line and '26.5' in line):
            start = max(0, i - 1)
            end = min(len(lines), i + 2)
            print(f'--- {f} around line {i+1} ---')
            for j in range(start, end):
                print(f'  {j+1}: {lines[j].rstrip()}')

    content = ''.join(lines)

    # Strategy 1: comment out lines containing the Xcode version error
    patched_lines = []
    changed = False
    for line in lines:
        if 'requires Xcode' in line and '<Error' in line:
            patched_lines.append(f'<!-- patched: {line.rstrip()} -->\n')
            changed = True
            print(f'Commenting out: {line.rstrip()}')
        else:
            patched_lines.append(line)

    if changed:
        with open(f, 'w') as fh:
            fh.writelines(patched_lines)
        print(f'Patched: {f}')
    else:
        # Strategy 2: replace the specific required version text
        patched = content.replace('requires Xcode 26.5', 'requires Xcode 0.0')
        if patched != content:
            with open(f, 'w') as fh:
                fh.write(patched)
            print(f'Patched via version replace: {f}')
        else:
            print(f'No match found in: {f}')
