import "server-only";

import { cache } from "react";
import type { CareerocApiUser } from "./api-user";

export type NormalizedCareerocApiUser = {
  email: string;
  name: string | null;
};

export function createUserScopedCache<TResult>(
  load: (user: NormalizedCareerocApiUser) => Promise<TResult>,
) {
  const cached = cache(async (email: string, name: string | null) => {
    return load({ email, name });
  });

  return (user: CareerocApiUser) => {
    const normalized = normalizeCareerocApiUser(user);
    return cached(normalized.email, normalized.name);
  };
}

export function createKeyedUserScopedCache<TResult>(
  load: (key: string, user: NormalizedCareerocApiUser) => Promise<TResult>,
) {
  const cached = cache(async (key: string, email: string, name: string | null) => {
    return load(key, { email, name });
  });

  return (key: string, user: CareerocApiUser) => {
    const normalized = normalizeCareerocApiUser(user);
    return cached(key, normalized.email, normalized.name);
  };
}

export function normalizeCareerocApiUser(
  user: CareerocApiUser,
): NormalizedCareerocApiUser {
  const normalizedName = user.name?.trim();
  return {
    email: user.email.trim().toLowerCase(),
    name: normalizedName ? normalizedName : null,
  };
}
