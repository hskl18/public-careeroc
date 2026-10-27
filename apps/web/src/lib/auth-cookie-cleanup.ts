const authCookieNames = [
  "authjs.session-token",
  "authjs.callback-url",
  "authjs.csrf-token",
  "authjs.pkce.code_verifier",
  "authjs.state",
  "authjs.nonce",
  "__Secure-authjs.session-token",
  "__Secure-authjs.callback-url",
  "__Host-authjs.csrf-token",
  "__Secure-authjs.pkce.code_verifier",
  "__Secure-authjs.state",
  "__Secure-authjs.nonce",
];

export function appendExpiredAuthCookies(headers: Headers) {
  for (const name of authCookieNames) {
    const secure = name.startsWith("__Secure-") || name.startsWith("__Host-");
    appendExpiredCookie(headers, name, secure, true);
    appendExpiredCookie(headers, name, secure, false);
  }
}

function appendExpiredCookie(
  headers: Headers,
  name: string,
  secure: boolean,
  httpOnly: boolean,
) {
  headers.append(
    "Set-Cookie",
    [
      `${name}=`,
      "Path=/",
      "Expires=Thu, 01 Jan 1970 00:00:00 GMT",
      "Max-Age=0",
      "SameSite=Lax",
      secure ? "Secure" : null,
      httpOnly ? "HttpOnly" : null,
    ]
      .filter(Boolean)
      .join("; "),
  );
}
