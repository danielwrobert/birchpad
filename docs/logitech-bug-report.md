# Bug report: importing a profile for a plugin's application fails

## Summary

Importing a `.lp5` profile whose application belongs to a plugin (an application name starting with `@_`) always fails with a generic error.
Logi Options+ sends `ImportNewApplicationProfile` with `processOrBundleName: null`, and Logi Plugin Service throws a `NullReferenceException`.
This also happens with a profile that Options+ exported itself, unchanged.

## Versions

- Logi Plugin Service 6.4.1.3246 (PluginApi 6.4.1.3246)
- macOS 27.0
- Device: MX Creative Keypad (`Loupedeck70`)
- Plugins built with the Actions SDK (C#), installed from `.lplug4` packages

## Steps

1. Install a plugin that declares `HasApplication` and links to a macOS app by bundle ID, for example `com.googlecode.iterm2`.
   Options+ creates the application `@_iterm`.
2. In Options+, export that application's profile to a `.lp5` file.
3. Import the same `.lp5` file.

## Expected

The profile is imported into the existing `@_iterm` application.

## Actual

Options+ shows a generic failure.
The service log (with the `enablelogs` marker file) shows:

```text
Parsing profile from '.../DevDeck iTerm.lp5'
Loaded profile 'Loupedeck70' '@_iterm' '120499B0FF355CDCA5235DEEFB97F41F'
[LME] Request: 'configui' 1436 'ImportNewApplicationProfile'
Importing profile from '.../DevDeck iTerm.lp5' to a new 'Loupedeck70' '' application
Telemetry 'package_install' for 'Profile5' '' '' GenericError ''
Cannot import profile
    (NullReferenceException) Arg_NullReferenceException
[LME] Request handler failed 'configui' 1436 'ImportNewApplicationProfile'
```

The request Options+ sends:

```json
{"name":"ImportNewApplicationProfile","parameters":{"fileName":".../DevDeck Claude.lp5","deviceType":"Loupedeck70","processOrBundleName":null,"applicationDisplayName":"@_claude","profileDisplayName":"DevDeck Claude Profile"},"options":{"importForActiveDevice":true}}
```

## Likely cause

Options+ does not match `@_` plugin applications to an existing application, so it takes the "import as a new application" path and sends no bundle ID.
The service already has `ImportApplicationProfile(profileFilePath, deviceType, applicationName, ...)`, which would import into `@_iterm`; Options+ should call that when the profile's `applicationName` exists.
Separately, `ImportNewApplicationProfile` could reject a null `processOrBundleName` with a clear error instead of throwing.
