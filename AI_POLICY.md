# AI Usage Policy

> [!NOTE]
> Adapted from [Ghostty's AI Usage Policy](https://github.com/ghostty-org/ghostty/blob/main/AI_POLICY.md).

The Magpie project has strict rules for AI usage:

- **All AI usage in any form must be disclosed.** You must state
  the tool you used (e.g. Claude Code, Cursor, Amp) along with
  the extent that the work was AI-assisted.

- **The human-in-the-loop must fully understand all code.** If you
  can't explain what your changes do and how they interact with the
  greater system without the aid of AI tools, do not contribute
  to this project.

- **Issues and discussions can use AI assistance but must have a full
  human-in-the-loop.** This means that any content generated with AI
  must have been reviewed _and edited_ by a human before submission.
  AI is very good at being overly verbose and including noise that
  distracts from the main point. Humans must do their research and
  trim this down.

- **No AI-generated media is allowed (art, images, videos, audio, etc.).**
  Text and code are the only acceptable AI-generated content, per the
  other rules in this policy.

These rules apply to all contributions to Magpie, including contributions
from maintainers.

## There are Humans Here

Please remember that Magpie is maintained by humans.

Every discussion, issue, and pull request is read and reviewed by
humans (and sometimes machines, too). It is a boundary point at which
people interact with each other and the work done. It is rude and
disrespectful to approach this boundary with low-effort, unqualified
work, since it puts the burden of validation on the maintainer.

In a perfect world, AI would produce high-quality, accurate work
every time. But today, that reality depends on the driver of the AI.
And today, most drivers of AI are just not good enough. So, until either
the people get better, the AI gets better, or both, we have to have
strict rules to protect maintainers.

## AI is Welcome Here

Magpie is written with plenty of AI assistance, and many maintainers embrace
AI tools as a productive tool in their workflow. As a project, we welcome
AI as a tool!

**Our reason for the strict AI policy is not due to an anti-AI stance**, but
instead due to the number of highly unqualified people using AI. It's the
people, not the tools, that are the problem.

I include this section to be transparent about the project's usage about
AI for people who may disagree with it, and to address the misconception
that this policy is anti-AI in nature.

## Commit authorship and format

Commits should contain exactly one `authorship:` line using a value from the table below.
When several commits are being squashed, intermediate commits may omit the authorship disclosures as long as they are included in the final squash commit.

`Co-Authored-By` trailers are not permitted.

| Value | Meaning | Required disclosure fields |
| --- | --- | --- |
| `human`, `artisanal`, or `meat` | Humans wrote all contributed content. AI assisted research and code navigation are allowed. | None. |
| `ai assisted` | Humans contributed detailed design, implementation direction, or original content; AI helped implement or write it. | `model:`, `ai use:`, `human work:`, `ai work:` |
| `human assisted` | AI wrote the content and a human reviewed and cleaned it up. | `model:`; other disclosure fields are optional. |

Models should be included as a comma-separated list. Human design and existing work should be credited alongside AI implementation, including tests, documentation, and configuration.

For either AI category, the commit message is required to end with this exact paragraph:

```text
a human reviewed and understood all changes
```

Commits with missing or invalid disclosures or a missing required review statement will be rejected outright.
