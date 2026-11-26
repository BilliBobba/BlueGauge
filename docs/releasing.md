# BlueGauge release checklist

When publishing a new version:

1. Choose a SemVer version and update the application version and changelog. Summarize user-visible changes and note verified device support separately from untested profiles.
2. Refresh both READMEs when the application structure, build or launch commands, supported-device list, or UI changes. If the UI changes, regenerate the English and Russian PNGs under `assets/` from that version's build.
3. Run `.\scripts\build.ps1 -Test`. When relevant hardware is available, also run `-Live` and verify actual readings; a successful diagnostic with zero detected devices is not proof of support. Smoke-test the exe from the extracted portable ZIP.
4. Commit and push the code, changelog, documentation and updated screenshots to `main`. Create a new `vX.Y.Z` tag on the exact version commit; do not move old release tags.
5. Publish a GitHub Release with release notes and the tested portable ZIP. Keep screenshots in the repository/README only: do not attach PNG files or embed screenshots in the release notes. Keep preview releases marked as prereleases. Use the latest stable release for the README download link.
6. Check the published release, its assets, and the tag on GitHub. Report the release link and what was verified.
