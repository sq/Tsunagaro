#if !defined(_VMULTI_CLIENT_H_)
#define _VMULTI_CLIENT_H_

#include "vmulticommon.h"

#ifdef VMULTI_BUILDING
#define VMULTI_EXPORT __declspec(dllexport)
#else
#define VMULTI_EXPORT __declspec(dllimport)
#endif

typedef struct _vmulti_client_t* pvmulti_client;

VMULTI_EXPORT pvmulti_client vmulti_alloc(void);

VMULTI_EXPORT void vmulti_free(pvmulti_client vmulti);

VMULTI_EXPORT BOOL vmulti_connect(pvmulti_client vmulti);

VMULTI_EXPORT void vmulti_disconnect(pvmulti_client vmulti);

VMULTI_EXPORT BOOL vmulti_update_mouse(pvmulti_client vmulti, BYTE button, USHORT x, USHORT y, BYTE wheelPosition);

VMULTI_EXPORT BOOL vmulti_update_relative_mouse(pvmulti_client vmulti, BYTE button, BYTE x, BYTE y, BYTE wheelPosition);

VMULTI_EXPORT BOOL vmulti_update_digi(pvmulti_client vmulti, BYTE status, USHORT x, USHORT y);

VMULTI_EXPORT BOOL vmulti_update_multitouch(pvmulti_client vmulti, PTOUCH pTouch, BYTE actualCount);

VMULTI_EXPORT BOOL vmulti_update_joystick(pvmulti_client vmulti, USHORT buttons, BYTE hat, BYTE x, BYTE y, BYTE rx, BYTE ry, BYTE throttle);

VMULTI_EXPORT BOOL vmulti_update_keyboard(pvmulti_client vmulti, BYTE shiftKeyFlags, BYTE keyCodes[KBD_KEY_CODES]);

VMULTI_EXPORT BOOL vmulti_write_message(pvmulti_client vmulti, VMultiMessageReport* pReport);

VMULTI_EXPORT BOOL vmulti_read_message(pvmulti_client vmulti, VMultiMessageReport* pReport);

#endif
