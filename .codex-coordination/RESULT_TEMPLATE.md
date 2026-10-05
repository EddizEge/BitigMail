# Successful result

TASK_ID:
TASK-XXX

STATUS:
DONE

EXECUTOR:
GEMINI

CONTROLLER:
SOL 5.6 LIMITED

CHANGED_FILES:
- <Path>

SUMMARY:
- <Short outcome>

VERIFICATION:
- tests: <PASS with command, or actual outcome>
- typecheck: <PASS with command, or actual outcome>
- lint: <PASS with command / NOT_REQUIRED with reason>
- build: <PASS with command / NOT_REQUIRED with reason>
- scope/dependencies: <Unexpected changes or none>

RISKS:
<none or brief list>

UNCERTAINTIES:
<none or brief list>

---

# Escalation result (use instead of successful result)

TASK_ID:
TASK-XXX

STATUS:
ESCALATED

CHANGED_FILES:
- <Path or none>

FAILED_VERIFICATION:
<Exact failing command, or reason verification is unavailable>

RELEVANT_ERROR:
<Minimal error and attempts made>

GEMINI_STATUS:
<BLOCKED | UNCERTAIN | FAILED>

WHY_ESCALATED:
<Decision or blocker>

RECOMMENDED_NEXT_STEP:
<Concrete next action>
