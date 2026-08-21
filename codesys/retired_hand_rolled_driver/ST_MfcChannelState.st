TYPE ST_MfcChannelState :
STRUCT
    iSubState       : INT;
    iRetryCount     : INT;
    wFaultCode      : WORD;
    xRegulating     : BOOL;
    xFaultActive    : BOOL;
    xCloseConfirmed : BOOL;
    rMeasuredSccm   : REAL;
END_STRUCT
END_TYPE
