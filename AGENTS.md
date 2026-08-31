# Repository Agent Instructions

These instructions apply to the entire repository.

## Pull requests

- Agents must never merge or close a pull request, enable auto-merge for one,
  or call any API or command that can merge or close one. Only a human
  repository owner may perform those actions.
- Agents may create and update branches and pull requests, review changes,
  monitor checks, and report when a pull request is ready. Stop at that point
  and hand the merge decision to the user.
- `main` accepts pull requests through the merge-commit method only. Do not use
  squash merging or rebase merging.
- Commits within a pull request must have linear history. Rebase the pull
  request branch onto `main`; never merge `main` into the pull request branch.
- Never bypass branch protection, force-push `main`, or disable required checks.

## Privacy

- Never commit real chat exports, message text, attachment media, screenshots
  containing conversations, machine-specific paths, credentials, or secrets.
- Use synthetic, redacted fixtures for tests and documentation.
