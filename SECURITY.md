# Security policy

Email **developer@lunixi.com** with subject `Security: Lunixi nopCommerce` for private vulnerability reports. Include plugin/nopCommerce/.NET versions, sanitized request reference, impact and a minimal reproduction. Never include merchant private keys, webhook secrets, browser tokens, customer data or raw card information. Do not publish vulnerabilities in public issues.

Use current supported platform and plugin releases. Store a backed-up 32-byte random encryption key on each application server as `LUNIXI_NOP_ENCRYPTION_KEY`, encoded in base64 and kept outside the database/source repository. Losing or replacing that key makes historical encrypted credentials unreadable. Plan credential/key retention through refund and reconciliation periods.

Configure HTTPS and trusted proxy handling. Do not enable HTTP body/header logging for checkout or gateway requests. The plugin does not log credentials or full response bodies. Third-party native event consumers must queue external effects durably; they must not interpret an uncommitted native transaction as final fulfillment.

Webhook signature verification alone is insufficient: the plugin matches the durable local attempt and reads authenticated gateway settlement evidence before applying native state. Browser callbacks never mark an order paid. Unknown financial outcomes retain the original operation key. Uninstall preserves attempts, operations, inbox and encrypted profiles.
