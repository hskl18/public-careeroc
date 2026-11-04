import { mkdir, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { encode } from "next-auth/jwt";

const outputPath = resolve(
  process.cwd(),
  process.argv[2] ?? "e2e/.auth/demo-user.json",
);
const secret =
  process.env.AUTH_SECRET ??
  process.env.NEXTAUTH_SECRET ??
  "dummy-nextauth-secret-for-local-e2e-123456";
const email = process.env.DEMO_AUTH_EMAIL ?? "demo@example.com";
const name = process.env.DEMO_AUTH_NAME ?? "careeroc demo";
const cookieName = "authjs.session-token";
const expires = Math.floor(Date.now() / 1000) + 60 * 60 * 8;

const value = await encode({
  secret,
  salt: cookieName,
  token: {
    email,
    name,
    sub: email,
    iat: Math.floor(Date.now() / 1000),
    exp: expires,
    jti: crypto.randomUUID(),
  },
});

const storageState = {
  cookies: [
    {
      name: cookieName,
      value,
      domain: "localhost",
      path: "/",
      expires,
      httpOnly: true,
      secure: false,
      sameSite: "Lax",
    },
  ],
  origins: [],
};

await mkdir(dirname(outputPath), { recursive: true });
await writeFile(outputPath, `${JSON.stringify(storageState, null, 2)}\n`);
console.log(`Wrote ${outputPath}`);
