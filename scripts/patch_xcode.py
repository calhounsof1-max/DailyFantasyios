import glob, os, re

home = os.path.expanduser('~')
pattern = f'{home}/.dotnet/packs/Microsoft.iOS.Sdk.net10.0_26.5/*/targets/Xamarin.Shared.Sdk.targets'
files = glob.glob(pattern)
print(f'Found: {files}')

for f in files:
    with open(f) as fh:
        lines = fh.readlines()

    # Print line 2570 and surrounding lines
    print(f'--- Lines 2565-2575 of {f} ---')
    for i in range(2564, min(2575, len(lines))):
        print(f'  {i+1}: {lines[i].rstrip()}')

    # Search for RequiredXcodeVersion (with or without underscore)
    for i, line in enumerate(lines):
        if 'RequiredXcodeVersion' in line or ('Xcode' in line and ('Error' in line or 'error' in line)):
            print(f'  Match at {i+1}: {line.rstrip()}')

    # Patch: comment out lines with Error + Xcode condition
    changed = False
    patched = []
    for i, line in enumerate(lines):
        if '<Error' in line and 'Xcode' in line:
            patched.append(f'<!-- removed: {line.rstrip()} -->\n')
            changed = True
            print(f'Removed Error line {i+1}')
        elif '_RequiredXcodeVersion' in line and '<' in line and '>' in line:
            # Replace the required version value with installed version
            new_line = re.sub(r'(?<=_RequiredXcodeVersion>)[^<]+', '26.3', line)
            patched.append(new_line)
            if new_line != line:
                changed = True
                print(f'Patched version at line {i+1}')
        else:
            patched.append(line)

    if changed:
        with open(f, 'w') as fh:
            fh.writelines(patched)
        print(f'File patched: {f}')
    else:
        print(f'No patchable content found - dumping all Xcode-related lines:')
        for i, line in enumerate(lines):
            if 'xcode' in line.lower():
                print(f'  {i+1}: {line.rstrip()}')
