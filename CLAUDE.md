# Working in this repository

## Line endings: CRLF, and it bites

`.gitattributes` sets `* text=auto eol=crlf`, and `.editorconfig` sets
`end_of_line = crlf` for code (`lf` only for `*.yml` / `*.yaml`). So every C#
file is stored **LF in the git index** and checked out **CRLF in the working
tree**. That is normal and correct — do not try to "fix" it.

The trap: a tool that writes a new file with LF endings leaves the working tree
holding LF while the attribute says CRLF. The commit itself is clean (git
normalises to LF on the way in, like every other file), but the local
`dotnet format --verify-no-changes` fails with a wall of

```
error ENDOFLINE: Fix end of line marker. Replace 1 characters with '\r\n'
```

which reads like a code defect and is not one. Check with:

```bash
git ls-files --eol | grep -E "^i/lf\s+w/lf" | grep -vE "\.ya?ml$"
```

Anything listed (YAML excepted) has the wrong endings in the working tree. Fix
by re-checking-out from the index:

```bash
rm -f <paths> && git checkout -- <paths>
```

When creating C# files, write them with CRLF, or re-checkout right after
committing.

## CI: the format gate runs first

`.github/workflows/build.yml` order is Restore → **Verify formatting** → Build →
Test. `dotnet format --verify-no-changes` is a hard gate: when it fails, Build
and Test are *skipped*, so the run is red without a single line having been
compiled. A red `build` here usually means formatting, not a broken build —
check which step failed before assuming the code is at fault.

## CI: the SDK floats, so the format gate can go red on its own

Both workflows pin `dotnet-version: 8.0.x`, and `global.json` uses
`"version": "8.0.0"` with `"rollForward": "latestFeature"`. Neither pins a
patch, so `setup-dotnet` installs whatever 8.x the runner offers that day.
`dotnet format` ships with the SDK and its rules change between releases —
which means **this gate can turn red with no commit at all**.

Recorded instance: run `32473049428` (2026-08-21, `main` @ `6e9ccc5`) failed at
Verify formatting, while run `27361027297` (2026-06-11, `main` @ `99e1794`)
passed. The two trees are byte-identical — `git diff 99e1794 6e9ccc5` is empty;
the commits differ only in the author email after a history rewrite. Nothing in
the code changed between green and red.

So before debugging code for a `Verify formatting` failure, compare the SDK
version the failing run used against a previously green run. The durable fix is
to pin an exact patch (`dotnet-version: 8.0.424`, or `global.json` with
`rollForward: patch`/`disable`) so formatting rules move only when the pin is
deliberately bumped.

## Verifying locally before pushing

Run what CI runs, in the same order:

```bash
dotnet format WhisperTray.sln --verify-no-changes --no-restore
```

```bash
dotnet build WhisperTray.sln --configuration Release
```

```bash
dotnet test WhisperTray.sln --configuration Release
```

## Commit identity

`user.email` is set **locally** in this repository to `amphasis@yandex.ru`,
because the global config carries a work address. If commits start showing the
wrong author, check `git config --local --get user.email` before anything else.
