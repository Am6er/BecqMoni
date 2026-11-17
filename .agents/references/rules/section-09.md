<!-- Контекст Codex; действующие правила — в корневом AGENTS.md. -->

## GitHub

For anything involving GitHub — reading/reviewing/commenting on PRs, issues, releases, CI runs —
use the **`gh` CLI**, which is installed and authenticated on the machine (run it through the
persistent `pwsh` channel, not the Linux sandbox — the sandbox has no auth and no network to
GitHub). Do not scrape github.com in a browser for things `gh` can do.

```powershell
gh pr list                      # open PRs
gh pr view <n> --comments       # PR body + review comments
gh pr diff <n>                  # the diff
gh pr review <n> --comment -b "..."   # reply / review
gh issue list ; gh issue view <n>
gh run list ; gh run view <id> --log-failed
gh api repos/:owner/:repo/...   # anything not covered by a subcommand
```
