---
name: ardis-cvdcore-working-style
description: "How the user wants work done on ArdisCVDCore — Russian only, no comments in code, don't build until told, ask instead of inventing"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 16643833-33f2-49b6-b3fc-cccf7a716994
  modified: 2026-08-25T00:00:00.000Z
---

Four standing instructions on this project.

**Never write comments in code.** Stated as an absolute on 25.08.2026: "ТЕБЕ ЗАПРЕЩЕНО писать комментарии в .st и C# коде, пока я тебе этого не скажу". Applies to both `.st` and `.cs`, and to doc comments (`(* *)`, `//`, `///`) alike. It holds until the user lifts it explicitly — not per task, not per file. Existing comments written before the ban were left in place; ask before stripping any.

**Answer in Russian only.** Stated in the very first message and never relaxed. Code and UI text stay in English.

**Do not build or run the app until asked.** The rhythm is: make the edits, stop, report; the user then says "собери, давай проверим" and only then comes MSBuild plus a screenshot. They repeated "пока не собирай" several times, including mid-turn, so treat silence as "don't build" during a run of edits.

**One step at a time when diagnosing.** Said on 26.08.2026: "давай как-то по порядку, а то ты мне вываливаешь сразу море информации, я не могу так сориентироваться". Long structured answers with tables, numbered causes and parallel checklists are counter-productive here — the user is standing at the machine with CODESYS open, not reading a report. Give ONE action, say where to click, say what to report back, stop. Wait for the answer before the next step. This applies to hardware/protocol debugging especially; it does not mean hiding a real caveat, it means not front-loading five of them.

**Ask rather than invent.** Said explicitly more than once ("сам ничего не выдумывай и задавай больше уточняющих вопросов"). This is a reactor control HMI, and guessing a threshold, a unit or a scaling has physical consequences. Batch the genuinely blocking questions, keep building everything that does not depend on the answers, and state assumptions plainly where a choice was unavoidable.

**Why the comment ban:** `.st` files in the repo are a hand-carried mirror of `ArdisControl.project`, not its source — text moves between CODESYS and the repo by copy-paste in both directions, and comments make that paste noisy and easy to get wrong. It has already gone wrong once: on 25.08.2026 a paste-back left `PLC_PRG.st` comment-stripped, with a duplicated traffic-light block and a lost watchdog. The user reads the diff, not the prose.

**How to apply:** the reasoning that would have gone into a comment goes into these memory files instead — that is what the user asked for ("все важные моменты всегда запоминай к себе в память, чтобы на следующий день не терять суть проекта"). Register numbers, bit layouts, which flag means what, why a branch exists: [[ardis-cvdcore-open-work]] and the reference memories. Explain the change in the chat reply, not in the file.

Also: when a design references a signal, check first whether the program actually reads it. If it does, wire it. If the gap is a missing *policy* (what an abort does, what a percentage is measured against) rather than a missing measurement, that is a question — and say which of the two it is, because the user pushes back when a group is disabled on the vaguer grounds of "no backend".

See [[ardis-cvdcore-open-work]].
