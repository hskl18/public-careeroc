import NextAuth from "next-auth";
import Credentials from "next-auth/providers/credentials";
import Google from "next-auth/providers/google";
import type { Provider } from "next-auth/providers";

import { fetchCareerocApi } from "@/lib/careeroc-api-server";
import { isAccessAllowed } from "@/lib/access-policy";
import { createDemoIdentity, defaultDemoEmailDomain, isDemoEmail } from "@/lib/demo";
import { isAllowedProductionUrl } from "@/lib/url-policy";

const isProduction = process.env.NODE_ENV === "production";
const devAuthSecret = "careeroc-dev-auth-secret-change-me";
const minimumSecretLength = 32;

const authSecret =
  process.env.AUTH_SECRET ??
  process.env.NEXTAUTH_SECRET ??
  (!isProduction ? devAuthSecret : undefined);

const googleClientId =
  process.env.AUTH_GOOGLE_ID ?? process.env.Google__ClientId;
const googleClientSecret =
  process.env.AUTH_GOOGLE_SECRET ?? process.env.Google__ClientSecret;
const authBaseUrl = process.env.AUTH_URL ?? process.env.NEXTAUTH_URL;
const accessAllowedEmails = parseAccessSet(process.env.ACCESS_ALLOWED_EMAILS);
const accessAllowedDomains = parseAccessSet(process.env.ACCESS_ALLOWED_DOMAINS).map(
  (domain) => domain.replace(/^@+/, ""),
);
const accessAllowAllUsers = parseBoolean(
  process.env.ACCESS_ALLOW_ALL_USERS ?? process.env.Access__AllowAllUsers,
);

export const isDemoMode = parseBoolean(process.env.DEMO_MODE);
export const demoEmailDomain =
  process.env.DEMO_EMAIL_DOMAIN?.trim().toLowerCase() || defaultDemoEmailDomain;

const providers: Provider[] = [];

if (isDemoMode) {
  providers.push(
    Credentials({
      id: "demo",
      name: "Demo workspace",
      credentials: {},
      authorize: authorizeDemoVisitor,
    }),
  );
}

if (googleClientId && googleClientSecret) {
  providers.push(
    Google({
      clientId: googleClientId,
      clientSecret: googleClientSecret,
      authorization: {
        params: {
          prompt: "select_account",
        },
      },
    }),
  );
}

export const isGoogleAuthConfigured = Boolean(
  googleClientId && googleClientSecret,
);
export const isAuthEnabled = Boolean(
  authSecret && ((googleClientId && googleClientSecret) || isDemoMode),
);
export const signInLabel = isDemoMode ? "Open the demo" : "Start with Google";

assertProductionAuthConfiguration();

const secureCookiePrefix = isProduction ? "__Secure-" : "";
const csrfCookieName = isProduction
  ? "__Host-authjs.csrf-token"
  : "authjs.csrf-token";

export const { handlers, auth, signIn, signOut } = NextAuth({
  trustHost: true,
  secret: authSecret,
  useSecureCookies: isProduction,
  logger: {
    error(error) {
      if (getAuthErrorType(error) === "JWTSessionError") {
        return;
      }

      console.error(error);
    },
  },
  pages: {
    signIn: "/",
    error: "/",
  },
  session: {
    strategy: "jwt",
    maxAge: 60 * 60 * 12,
    updateAge: 60 * 60,
  },
  cookies: {
    sessionToken: {
      name: `${secureCookiePrefix}authjs.session-token`,
      options: buildCookieOptions(),
    },
    callbackUrl: {
      name: `${secureCookiePrefix}authjs.callback-url`,
      options: buildCookieOptions({ httpOnly: false }),
    },
    csrfToken: {
      name: csrfCookieName,
      options: buildCookieOptions(),
    },
    pkceCodeVerifier: {
      name: `${secureCookiePrefix}authjs.pkce.code_verifier`,
      options: buildCookieOptions(),
    },
    state: {
      name: `${secureCookiePrefix}authjs.state`,
      options: buildCookieOptions(),
    },
    nonce: {
      name: `${secureCookiePrefix}authjs.nonce`,
      options: buildCookieOptions(),
    },
  },
  providers,
  callbacks: {
    signIn({ account, profile, user }) {
      if (account?.provider === "demo") {
        return isDemoMode && isDemoEmail(user?.email, demoEmailDomain);
      }

      if (account?.provider !== "google") {
        return false;
      }

      const email = typeof profile?.email === "string" ? profile.email : null;
      const emailVerified =
        typeof profile?.email_verified === "boolean"
          ? profile.email_verified
          : true;

      return Boolean(email) && emailVerified && isUserAccessAllowed(email);
    },
    session({ session }) {
      if (!session.user) {
        return session;
      }

      if (!session.user.name && session.user.email) {
        session.user.name = session.user.email.split("@")[0];
      }

      return session;
    },
  },
});

// One sign-in entry point: demo deployments open a fresh demo workspace, others go through Google.
export async function startSignIn(redirectTo: string) {
  if (isDemoMode) {
    return signIn("demo", { redirectTo });
  }

  return signIn("google", { redirectTo }, { prompt: "select_account" });
}

async function authorizeDemoVisitor() {
  const identity = createDemoIdentity(demoEmailDomain);
  const response = await fetchCareerocApi(
    "/api/demo/workspace",
    { email: identity.email, name: identity.name },
    { method: "POST", cache: "no-store" },
  );

  if (!response.ok) {
    console.error(`Demo workspace provisioning failed with HTTP ${response.status}.`);
    return null;
  }

  return identity;
}

export async function getOptionalSession() {
  if (!isAuthEnabled) {
    return null;
  }

  return auth();
}

export function isUserAccessAllowed(email?: string | null) {
  return isAccessAllowed(email, {
    allowAllUsers: accessAllowAllUsers,
    allowedEmails: accessAllowedEmails,
    allowedDomains: accessAllowedDomains,
    isProduction,
    demoEmailDomain: isDemoMode ? demoEmailDomain : null,
  });
}

function assertProductionAuthConfiguration() {
  // Images are built once and configured at runtime, so the checks run when the server starts.
  if (!isProduction || process.env.NEXT_PHASE === "phase-production-build") {
    return;
  }

  if (!authSecret) {
    throw new Error(
      "careeroc authentication is not configured for production. Set AUTH_SECRET or NEXTAUTH_SECRET.",
    );
  }

  if (
    authSecret === devAuthSecret ||
    authSecret.trim().length < minimumSecretLength
  ) {
    throw new Error(
      "careeroc authentication secret must be at least 32 characters and must not use the development fallback in production.",
    );
  }

  // docker-compose.yml ships public fallback secrets; with them anyone could forge a real account's session.
  if (
    isGoogleAuthConfigured &&
    [authSecret, process.env.CAREEROC_API_AUTH_SECRET].some((secret) =>
      secret?.startsWith("local-compose-only-"),
    )
  ) {
    throw new Error(
      "AUTH_SECRET and CAREEROC_API_AUTH_SECRET still use the public fallbacks from docker-compose.yml. Set your own random values before enabling Google sign-in.",
    );
  }

  if (!isDemoMode && (!googleClientId || !googleClientSecret)) {
    throw new Error(
      "careeroc Google OAuth credentials must be configured in production.",
    );
  }

  if (!authBaseUrl) {
    throw new Error(
      "careeroc authentication requires AUTH_URL or NEXTAUTH_URL in production.",
    );
  }

  let parsedAuthBaseUrl: URL;

  try {
    parsedAuthBaseUrl = new URL(authBaseUrl);
  } catch {
    throw new Error(
      "careeroc authentication base URL must be an absolute HTTPS URL in production.",
    );
  }

  if (!isAllowedProductionUrl(parsedAuthBaseUrl, "public")) {
    throw new Error(
      "careeroc authentication base URL must use HTTPS in production.",
    );
  }

  // Demo identities are checked by isDemoEmail; the allowlist only governs Google accounts.
  if (
    isGoogleAuthConfigured &&
    !accessAllowAllUsers &&
    accessAllowedEmails.length === 0 &&
    accessAllowedDomains.length === 0
  ) {
    throw new Error(
      "careeroc requires an explicit production access policy. Set ACCESS_ALLOWED_EMAILS or ACCESS_ALLOWED_DOMAINS, or ACCESS_ALLOW_ALL_USERS=true only for an intentionally open deployment.",
    );
  }
}

function parseAccessSet(value?: string | null) {
  return (value ?? "")
    .split(/[,;\n\r]/)
    .map((item) => item.trim().toLowerCase())
    .filter(Boolean);
}

function parseBoolean(value?: string | null) {
  return /^(1|true|yes)$/i.test(value?.trim() ?? "");
}

function buildCookieOptions(overrides?: { httpOnly?: boolean }) {
  return {
    httpOnly: overrides?.httpOnly ?? true,
    sameSite: "lax" as const,
    path: "/",
    secure: isProduction,
  };
}

function getAuthErrorType(error: Error) {
  return "type" in error && typeof error.type === "string"
    ? error.type
    : error.name;
}
