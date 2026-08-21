TYPE ST_GasChannelState :
STRUCT
    eInit            : E_GasChannelInit;
    iInitDelay       : INT;
    xRegulating      : BOOL;
    xFaultActive     : BOOL;
    xCloseConfirmed  : BOOL;
    iLastSetpointRaw : INT;
END_STRUCT
END_TYPE
