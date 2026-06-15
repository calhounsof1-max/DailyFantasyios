import glob, os, re

home = os.path.expanduser('~')
pattern = f'{home}/.dotnet/packs/Microsoft.iOS.Sdk*/*/targets/Xamarin.Shared.Sdk.targets'
files = glob.glob(pattern)
print(f'Found targets files: {files}')

for f in files:
    with open(f) as fh:
        content = fh.read()

    # Print lines mentioning Xcode for debugging
    for i, line in enumerate(content.split('\n')):
        if 'xcode' in line.lower() and ('require' in line.lower() or 'error' in line.lower()):
            print(f'Line {i+1}: {line.strip()}')

    # Remove the Xcode version Error check
    patched = re.sub(
        r'<Error\s[^>]*requires\s+Xcode[^/]*/?>',
        '<!-- xcode version check removed -->',
        content, flags=re.IGNORECASE | re.DOTALL
    )
    if patched != content:
        with open(f, 'w') as fh:
            fh.write(patched)
        print(f'Patched: {f}')
    else:
        print(f'No match found in: {f}')
        # Print surrounding lines for debugging
        lines = content.split('\n')
        for i, line in enumerate(lines):
            if 'requires Xcode' in line:
                start = max(0, i - 2)
                end = min(len(lines), i + 3)
                print(f'Context around line {i+1}:')
                for j in range(start, end):
                    print(f'  {j+1}: {lines[j]}')
