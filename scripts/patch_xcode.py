import glob, os, re, subprocess

home = os.path.expanduser('~')

# ── 1. Print xcrun iphoneos SDK version BEFORE plist patch ──────────────────
result = subprocess.run(['xcrun', '--sdk', 'iphoneos', '--show-sdk-version'],
                        capture_output=True, text=True)
print(f'xcrun iphoneos SDK version (before): {result.stdout.strip()} {result.stderr.strip()}')

result2 = subprocess.run(['xcrun', '--sdk', 'iphoneos', '--show-sdk-path'],
                         capture_output=True, text=True)
sdk_path = result2.stdout.strip()
print(f'xcrun iphoneos SDK path: {sdk_path}')

# ── 2. Patch Xamarin.Shared.Sdk.targets ─────────────────────────────────────
pattern = f'{home}/.dotnet/packs/Microsoft.iOS.Sdk.net10.0_26.5/*/targets/Xamarin.Shared.Sdk.targets'
files = glob.glob(pattern)
print(f'Found targets: {files}')

for f in files:
    with open(f) as fh:
        lines = fh.readlines()

    # Diagnostics: print ALL lines that mention Xcode OR SDK version
    print(f'\n--- SDK/Version-related lines in {f} ---')
    for i, line in enumerate(lines):
        low = line.lower()
        if any(k in low for k in ['sdkversion', 'iossdkversion', '_sdkversion', 'xcodeversion',
                                    'requirexcode', 'requireddotnet', 'sdkdevroot',
                                    'iossdk', 'mtouchsdkversion', 'iphoneos',
                                    'xcode' , 'sdk_ver']):
            print(f'  {i+1}: {line.rstrip()}')

    # ── Patch 1: comment out <Error> lines with Xcode condition ──────────────
    changed = False
    patched = []
    for i, line in enumerate(lines):
        if '<Error' in line and 'Xcode' in line:
            patched.append(f'<!-- removed: {line.rstrip()} -->\n')
            changed = True
            print(f'Removed Error line {i+1}')
        elif '_RequiredXcodeVersion' in line and '<' in line and '>' in line:
            new_line = re.sub(r'(?<=_RequiredXcodeVersion>)[^<]+', '26.3', line)
            patched.append(new_line)
            if new_line != line:
                changed = True
                print(f'Patched _RequiredXcodeVersion at line {i+1}')
        else:
            patched.append(line)

    lines = patched  # use patched lines for next pass

    # ── Patch 2: override _iOSSdkVersion / _SdkVersion to "26.5" ─────────────
    # MAUI reads the SDK version from the directory name (e.g. iPhoneOS26.2.sdk).
    # We override the computed property to bypass the iOS 26.4+ type availability check.
    patched2 = []
    version_patched = False
    for i, line in enumerate(lines):
        # Match any assignment of _iOSSdkVersion, _SdkVersion, MtouchSdkVersion
        # (only the computed/default assignment, not if already hardcoded)
        if re.search(r'<_iOSSdkVersion\b[^>]*>', line) and '26.5' not in line:
            # Comment out original and inject hardcoded version after
            patched2.append(f'<!-- sdk-ver-patch: {line.rstrip()} -->\n')
            patched2.append('    <_iOSSdkVersion>26.5</_iOSSdkVersion>\n')
            changed = True
            version_patched = True
            print(f'Patched _iOSSdkVersion at line {i+1}: {line.rstrip()}')
        elif re.search(r'<MtouchSdkVersion\b[^>]*>', line) and '26.5' not in line and 'Condition' not in line:
            patched2.append(f'<!-- sdk-ver-patch: {line.rstrip()} -->\n')
            patched2.append('    <MtouchSdkVersion>26.5</MtouchSdkVersion>\n')
            changed = True
            version_patched = True
            print(f'Patched MtouchSdkVersion at line {i+1}: {line.rstrip()}')
        else:
            patched2.append(line)

    if not version_patched:
        print('WARNING: _iOSSdkVersion / MtouchSdkVersion pattern not found in targets file.')
        print('  All iOSSdk / SdkVersion lines:')
        for i, line in enumerate(patched2):
            if re.search(r'(_iOSSdkVersion|_SdkVersion|MtouchSdkVersion)', line, re.IGNORECASE):
                print(f'    {i+1}: {line.rstrip()}')

    if changed:
        with open(f, 'w') as fh:
            fh.writelines(patched2)
        print(f'File patched: {f}')
    else:
        print(f'No patchable content found.')

# ── 3. Print xcrun iphoneos SDK version AFTER plist patch ───────────────────
# (The plist was already patched in the workflow Select Xcode step)
result3 = subprocess.run(['xcrun', '--sdk', 'iphoneos', '--show-sdk-version'],
                         capture_output=True, text=True)
print(f'\nxcrun iphoneos SDK version (after targets patch): {result3.stdout.strip()} {result3.stderr.strip()}')
