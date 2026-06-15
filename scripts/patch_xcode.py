import glob, os, re, subprocess

home = os.path.expanduser('~')
ios_pack = f'{home}/.dotnet/packs/Microsoft.iOS.Sdk.net10.0_26.5'

# ── 0. Print xcrun SDK version ───────────────────────────────────────────────
result = subprocess.run(['xcrun', '--sdk', 'iphoneos', '--show-sdk-version'],
                        capture_output=True, text=True)
print(f'xcrun iphoneos SDK version: {result.stdout.strip()}  (xcrun ignores plist; reads dir name)')

# ── 1. Patch Xamarin.Shared.Sdk.targets (Xcode version check) ───────────────
shared_pattern = f'{ios_pack}/*/targets/Xamarin.Shared.Sdk.targets'
shared_files = glob.glob(shared_pattern)
print(f'Found Xamarin.Shared.Sdk.targets: {shared_files}')

for f in shared_files:
    with open(f) as fh:
        lines = fh.readlines()

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

    if changed:
        with open(f, 'w') as fh:
            fh.writelines(patched)
        print(f'Xamarin.Shared.Sdk.targets patched: {f}')
    else:
        print(f'Xamarin.Shared.Sdk.targets: nothing to patch.')

# ── 2. Find and patch Microsoft.MaciOS.Sdk.Xcode.targets (_SdkVersion) ───────
# This file computes _SdkVersion from xcrun (which reads directory name, not plist).
# We override it to 26.5 so the ManagedRegistrar linker step accepts iOS 26.4+ types.
xcode_pattern = f'{ios_pack}/*/targets/Microsoft.MaciOS.Sdk.Xcode.targets'
xcode_files = glob.glob(xcode_pattern)
print(f'\nFound MaciOS.Sdk.Xcode.targets: {xcode_files}')

for f in xcode_files:
    with open(f) as fh:
        lines = fh.readlines()

    # Print ALL lines that mention SdkVersion, iOSSdk, MtouchSdk, Sdk version etc.
    print(f'\n--- SdkVersion-related lines in {f} ---')
    for i, line in enumerate(lines):
        low = line.lower()
        if any(k in low for k in ['sdkversion', 'iossdkversion', '_sdkversion',
                                    'mtouchsdkversion', 'iossdk', 'sdk_ver',
                                    'show-sdk-version', 'show-sdk-path', 'iphoneos',
                                    'sdk_platform_version', 'platformversion',
                                    'deploymenttarget', 'minimum-deployment']):
            print(f'  {i+1}: {line.rstrip()}')

    # Patch: replace any assignment of _SdkVersion computed from xcrun/regex
    # with a hardcoded "26.5"
    changed = False
    patched = []
    for i, line in enumerate(lines):
        # Match lines that SET _SdkVersion (not lines that USE it)
        if re.search(r'<_SdkVersion\b', line) and '26.5' not in line:
            patched.append(f'<!-- sdk-ver-patch: {line.rstrip()} -->\n')
            patched.append('      <_SdkVersion>26.5</_SdkVersion>\n')
            changed = True
            print(f'Patched _SdkVersion at line {i+1}: {line.rstrip()}')
        elif re.search(r'<MtouchSdkVersion\b', line) and '26.5' not in line:
            patched.append(f'<!-- sdk-ver-patch: {line.rstrip()} -->\n')
            patched.append('      <MtouchSdkVersion>26.5</MtouchSdkVersion>\n')
            changed = True
            print(f'Patched MtouchSdkVersion at line {i+1}: {line.rstrip()}')
        else:
            patched.append(line)

    if changed:
        with open(f, 'w') as fh:
            fh.writelines(patched)
        print(f'MaciOS.Sdk.Xcode.targets patched: {f}')
    else:
        print(f'MaciOS.Sdk.Xcode.targets: _SdkVersion not found by pattern.')
        print('  All lines with "<_" (private properties):')
        for i, line in enumerate(lines):
            if re.search(r'<_[A-Z][a-zA-Z]*Sdk|<_Sdk|<_iOS', line):
                print(f'    {i+1}: {line.rstrip()}')

# ── 3. Also search ALL targets files for _SdkVersion ─────────────────────────
all_targets = glob.glob(f'{ios_pack}/*/targets/*.targets')
print(f'\n--- Searching {len(all_targets)} targets files for _SdkVersion setter ---')
for f in all_targets:
    with open(f) as fh:
        content = fh.read()
    if '<_SdkVersion' in content or '<MtouchSdkVersion' in content:
        fname = os.path.basename(f)
        for i, line in enumerate(content.splitlines()):
            if '<_SdkVersion' in line or '<MtouchSdkVersion' in line:
                print(f'  {fname}:{i+1}: {line.strip()}')
