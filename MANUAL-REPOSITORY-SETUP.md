# Create your own repository from this source copy

This is a source-only distribution. It contains no `.git` directory or inherited commit history, and no prebuilt assemblies or NuGet packages. Generic AI-assisted code-development disclosure and required licence/third-party notices remain. The working-draft preparation headers have been generalised; executable code, schema, fixtures and normative requirements are unchanged.

## Before your first commit

1. Extract the ZIP and use the extracted `aps-dda/` folder, not a clone or fork of the previous repository.
2. Choose an unused repository name under your GitHub account. Create an **empty** repository with that name. Do not initialise it with a separate README, licence or `.gitignore` if using the command sequence below; these files already exist in this copy.
3. Update the project repository URL in `Directory.Build.props`, `SOURCE.md`, `MAINTAINERS.md`, README files and other relevant documents. Current links still identify the previous distribution at `https://github.com/kunal-y-abi/aps-dda`; the destination cannot be finalised until you choose a new repository name. Do not change APS-DDA artifact IDs, schema identifiers or C# namespaces merely to rename the GitHub repository.
4. Previous CI-run links and verification dates describe the earlier distribution. Run CI in your new repository and record its new results. Do not transplant old Actions run IDs into a new repository URL. The PDF editions use relative links for the project documents; external reference citations are retained.
5. Keep `LICENSE`, `LICENSING.md`, `LICENSES/`, `package-license.md` and the upstream source/vector notices. They are part of the applicable licence terms, not optional branding.

## Commit using your own identity

GitHub Desktop is suitable if you prefer a graphical workflow. Sign in to your GitHub account, initialise a repository in the extracted folder, confirm the author name/email in settings, make your initial commit and publish to the intended repository.

Alternatively, in PowerShell or another terminal, run the following from the extracted folder. Replace the email and repository-name placeholders **before running**. The `git config` commands below are repository-local, not global.

```sh
git init -b main
git config user.name "Kunal Yadav"
git config user.email "YOUR_GITHUB_VERIFIED_EMAIL_OR_NOREPLY_ADDRESS"
git add .
git commit -m "Initial APS-DDA public preview"
git remote add origin https://github.com/kunal-y-abi/YOUR_NEW_REPOSITORY.git
git push -u origin main
```

You can find your verified or GitHub-provided no-reply email in your GitHub email settings. Use your own GitHub authentication when prompted.

## Check the new publication

Confirm the first commit shows your intended author, the repository URL is correct and the new GitHub Actions run finishes successfully. The root README contains the .NET 10 build/demo/test commands. Build packages in the new repository after the initial commit, so package and assembly provenance refers to your new history. Recreate releases and checksums rather than re-uploading binaries from the previous repository.

The previous repository and its already-public history are not altered by this source copy. Retire or change its visibility only after you have verified the new repository, and only if you decide to do so. A new repository does not guarantee erasure of previously public copies or cached records.
