# ExtWeigh Microsoft Store submission pack (en-US)

## Basic information

- Product name: `ExtWeigh`
- Language: `English (United States)`
- Suggested category: `Developer tools` or `Utilities & tools`
- License: `MIT`
- Suggested price: `Free`

## Listing

- Short and full descriptions: `store/listing-en-US.md`
- Support URL: `https://github.com/1llum1n4t1s/ExtWeigh/issues`
- Privacy policy: `https://extweigh.nephilim.jp/privacy`
- Applicable license terms: `https://github.com/1llum1n4t1s/ExtWeigh/blob/main/LICENSE`
- 1:1 Store logo: `icon/app_icon.png`

## Package

- App type: `EXE`
- Architecture: `x64`
- Installer parameters: `--silent`
- Package URL: `https://extweigh.nephilim.jp/ExtWeigh-<VERSION>-win-x64-Setup.exe`
- Google Chrome: External dependency; auto-detected, with a configurable Chrome for Testing path
- Non-Microsoft drivers or NT services: `None`
- Bundleware: `None`

## Data use

- User account: `None`
- Telemetry or analytics SDK: `None`
- Advertising: `None`
- Personal data sent to the publisher: `None`
- Measurement data: `Stored locally only`
- Distribution site: Standard technical request data may be processed by Cloudflare infrastructure

## Draft certification notes

The app loads a user-selected unpacked Manifest V3 extension in Chrome and measures enabled/disabled differences in CPU time, Long Tasks, and JavaScript heap for a specified URL. If Google Chrome is not auto-detected, use Settings to select Chrome or Chrome for Testing. Pages that require a signed-in browser session are outside the scope of the current version. The installer supports a silent per-user installation with the `--silent` parameter.

## Pre-submission checklist

- [ ] Enter the reserved product name and Partner Center publisher details
- [ ] Replace `<VERSION>` with the submitted version
- [ ] Verify that the versioned HTTPS package URL returns 200 and will never be overwritten
- [ ] Verify Authenticode signatures on Setup.exe and installed PE files
- [ ] Test `--silent` installation and clean uninstall on a clean machine
- [ ] Upload at least one real app screenshot; four or more are recommended
- [ ] Upload `icon/app_icon.png` as the 1:1 Store logo
- [ ] Publish the privacy policy and support URLs
- [ ] Complete age rating, category, pricing, and market selection in Partner Center
