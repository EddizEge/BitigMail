TASK_ID: TASK-042
STATUS: DONE — native UI verified, 0.9.2 installed and reopened for user
EXECUTOR: SOL DIRECT frontend/native shell; ASTRA critical private host protocol
GOAL: Fix native first-administrator creation after startup proof expires at five minutes.
CONTRACT: Native WebView trusted top-document request BITIGMAIL_REFRESH_SETUP -> owned shell generates32byteproof -> private stdin SETUP_PROOF lowerhex -> host onlyuninitialized/notdraining replaces expiringproof -> authenticated MAC acknowledgement -> trusted WebView response -> frontend setup POST. NoHTTPproofminting, noexpiryrelaxation, nosecretlogs. Boundedserializedrequests, usefulerrors, submitdisabled whilepending, 12-characterpasswordhint.
VERIFY: Expiredclock-to-refreshedproof+realcatalog bootstrap test; malformed/initialized/drainingreject; actualproductionhost protocol; actualWebView UI createadmin syntheticisolatedprofile thenlogin; frontendtargetedtests/type/lint/build; packaged0.9.2.
DELIVERY: Preserveuserprofile; root owns currentapp safeclose/update/reopen. Do notreaduserpassword, resetidentity, orusepersonalemail. Azure/Outlook10excluded.
