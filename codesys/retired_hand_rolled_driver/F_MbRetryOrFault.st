FUNCTION F_MbRetryOrFault : INT
VAR_INPUT
    iIssueState : INT;
    iFaultState : INT;
    iMaxRetries : INT;
END_VAR
VAR_IN_OUT
    iRetryCount : INT;
END_VAR

IF iRetryCount < iMaxRetries THEN
    iRetryCount := iRetryCount + 1;
    F_MbRetryOrFault := iIssueState;
ELSE
    iRetryCount := 0;
    F_MbRetryOrFault := iFaultState;
END_IF;
