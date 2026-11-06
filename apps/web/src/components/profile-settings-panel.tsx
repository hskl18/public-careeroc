"use client";

import { useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";
import { useRouter } from "next/navigation";
import {
  ProfileIcon,
  defaultProfileIcon,
  profileIconOptions,
} from "@/components/profile-icon";
import type {
  UpdateUserProfileRequest,
  UserProfile,
} from "@/lib/profile";
import { readErrorMessage as readSharedErrorMessage } from "@/lib/client-fetch";

type ProfileSettingsPanelProps = {
  initialProfile: UserProfile;
  loadedFromApi: boolean;
  proxyPrefix: string | null;
  email: string;
  displayName: string;
};

const timeZoneOptions = [
  { value: "America/Los_Angeles", label: "Pacific Time" },
  { value: "America/Denver", label: "Mountain Time" },
  { value: "America/Chicago", label: "Central Time" },
  { value: "America/New_York", label: "Eastern Time" },
  { value: "Etc/UTC", label: "UTC" },
  { value: "Europe/London", label: "London" },
  { value: "Europe/Berlin", label: "Berlin" },
  { value: "Asia/Shanghai", label: "Shanghai" },
  { value: "Asia/Tokyo", label: "Tokyo" },
  { value: "Asia/Seoul", label: "Seoul" },
  { value: "Asia/Singapore", label: "Singapore" },
  { value: "Asia/Kolkata", label: "India" },
] as const;

async function readErrorMessage(response: Response) {
  if (response.status === 404) {
    return "Settings are unavailable. Try again.";
  }

  if (response.status >= 500) {
    return "The profile API failed.";
  }

  return readSharedErrorMessage(response);
}

function normalizeNullable(value: string) {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

export function ProfileSettingsPanel({
  initialProfile,
  loadedFromApi,
  proxyPrefix,
  email,
  displayName,
}: ProfileSettingsPanelProps) {
  const router = useRouter();
  const [profile, setProfile] = useState(initialProfile);
  const [name, setName] = useState(initialProfile.name);
  const [timeZoneId, setTimeZoneId] = useState(initialProfile.timeZoneId);
  const [profileIcon, setProfileIcon] = useState(
    initialProfile.profileIcon ?? defaultProfileIcon,
  );
  const [location, setLocation] = useState(initialProfile.location ?? "");
  const [targetRole, setTargetRole] = useState(initialProfile.targetRole ?? "");
  const [isSaving, setIsSaving] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const hasChanges = useMemo(
    () =>
      name.trim() !== profile.name ||
      timeZoneId !== profile.timeZoneId ||
      profileIcon !== (profile.profileIcon ?? defaultProfileIcon) ||
      normalizeNullable(location) !== profile.location ||
      normalizeNullable(targetRole) !== profile.targetRole,
    [location, name, profile, profileIcon, targetRole, timeZoneId],
  );
  const canSave = Boolean(proxyPrefix) && hasChanges && !isSaving;
  const saveStatus = isSaving
    ? "Saving..."
    : error
      ? "Couldn't save. Try again."
      : hasChanges
        ? "Unsaved changes"
        : notice
          ? "Saved just now"
          : "All changes saved";
  const saveStatusClass = error
    ? "text-[var(--red)]"
    : hasChanges
      ? "text-[var(--accent)]"
      : "text-[var(--text-tertiary)]";
  useEffect(() => {
    queueMicrotask(() => {
      setProfile(initialProfile);
      setName(initialProfile.name);
      setTimeZoneId(initialProfile.timeZoneId);
      setProfileIcon(initialProfile.profileIcon ?? defaultProfileIcon);
      setLocation(initialProfile.location ?? "");
      setTargetRole(initialProfile.targetRole ?? "");
    });
  }, [initialProfile]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canSave || !proxyPrefix) return;

    const request: UpdateUserProfileRequest = {
      name: name.trim(),
      timeZoneId,
      profileIcon,
      location: normalizeNullable(location),
      targetRole: normalizeNullable(targetRole),
    };

    setIsSaving(true);
    setNotice(null);
    setError(null);

    try {
      const response = await fetch(`${proxyPrefix}/api/profile`, {
        method: "PATCH",
        headers: {
          "content-type": "application/json",
        },
        body: JSON.stringify(request),
      });

      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }

      const nextProfile = (await response.json()) as UserProfile;
      const normalizedProfile = {
        ...nextProfile,
        profileIcon: nextProfile.profileIcon ?? defaultProfileIcon,
      };
      setProfile(normalizedProfile);
      setName(nextProfile.name);
      setTimeZoneId(nextProfile.timeZoneId);
      setProfileIcon(normalizedProfile.profileIcon);
      setLocation(nextProfile.location ?? "");
      setTargetRole(nextProfile.targetRole ?? "");
      setNotice("Profile saved.");
      router.refresh();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Profile save failed.");
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <form
      className="card flex min-h-0 flex-col p-0"
      onSubmit={handleSubmit}
    >
      <div className="shrink-0 px-4 pt-4 sm:px-5 sm:pt-5">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex min-w-0 items-center gap-3">
            <ProfileIcon
              icon={profileIcon}
              label={`${displayName} profile icon`}
              className="h-12 w-12 rounded-md border border-[var(--border)] bg-[var(--surface-elevated)]"
            />
            <div className="min-w-0">
              <h2 className="truncate text-base font-semibold text-[var(--text-primary)]">
                {displayName}
              </h2>
              <p className="mt-1 break-all text-sm text-[var(--text-secondary)]">
                {email}
              </p>
            </div>
          </div>
          <div className="flex flex-col items-start gap-2 sm:items-end">
            <p className={`text-xs font-medium ${saveStatusClass}`}>
              {saveStatus}
            </p>
            <button type="submit" className="btn btn-primary btn-sm" disabled={!canSave}>
              {isSaving ? <><span className="spinner" /> Saving...</> : "Save changes"}
            </button>
          </div>
        </div>
      </div>

      {/* A container query, because the form shares its row with the token panel when the API is live. */}
      <div className="workspace-scroll-region @container flex flex-1 flex-col px-4 pt-4 pb-4 sm:px-5 sm:pb-5">
        <section className="shrink-0">
          <div className="grid gap-4 sm:grid-cols-2 @4xl:grid-cols-4">
          <label className="grid gap-2 sm:col-span-1">
            <span className="eyebrow">Name</span>
            <input
              className="input"
              value={name}
              onChange={(event) => setName(event.target.value)}
              maxLength={200}
              disabled={isSaving}
            />
          </label>

          <label className="grid gap-2 sm:col-span-1">
            <span className="eyebrow">Time zone</span>
            <select
              className="input"
              value={timeZoneId}
              onChange={(event) => setTimeZoneId(event.target.value)}
              disabled={isSaving}
            >
              {timeZoneOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>

          <label className="grid gap-2 sm:col-span-1">
            <span className="eyebrow">Location</span>
            <input
              className="input"
              value={location}
              onChange={(event) => setLocation(event.target.value)}
              maxLength={160}
              placeholder="Portland, OR"
              disabled={isSaving}
            />
          </label>

          <label className="grid gap-2 sm:col-span-1">
            <span className="eyebrow">Target role</span>
            <input
              className="input"
              value={targetRole}
              onChange={(event) => setTargetRole(event.target.value)}
              maxLength={200}
              placeholder="Software engineering intern"
              disabled={isSaving}
            />
          </label>
        </div>
      </section>

      <section className="mt-4 flex flex-1 flex-col border-t border-[var(--border)] pt-4">
        <h2 className="text-base font-semibold text-[var(--text-primary)]">Icon</h2>
        <div
          className="mt-2 grid flex-1 auto-rows-[minmax(2.75rem,1fr)] grid-cols-6 gap-1.5 sm:gap-2"
          role="radiogroup"
          aria-label="Profile icon"
        >
          {profileIconOptions.map((option) => {
            const selected = profileIcon === option.id;

            return (
              <button
                key={option.id}
                type="button"
                role="radio"
                aria-checked={selected}
                aria-label={option.label}
                title={option.label}
                className={`relative min-w-0 rounded-md border transition ${
                  selected
                    ? "border-[var(--accent)] ring-1 ring-[var(--accent)]"
                    : "border-[var(--border)] hover:border-[var(--border-hover)]"
                }`}
                disabled={isSaving}
                onClick={() => setProfileIcon(option.id)}
              >
                <ProfileIcon icon={option.id} decorative className="absolute inset-[12%] h-auto w-auto" />
              </button>
            );
          })}
        </div>
      </section>

      <div className="mt-4 flex shrink-0 flex-col gap-2 empty:hidden">
        <p role="status" className="text-sm font-medium text-[var(--green)] empty:hidden">
          {notice}
        </p>
        {error ? (
          <p role="alert" className="text-sm font-medium text-[var(--red)]">{error}</p>
        ) : null}
        {!proxyPrefix || (!loadedFromApi && !error && !notice) ? (
          <p className="text-sm text-[var(--text-tertiary)]">Offline.</p>
        ) : null}
      </div>
      </div>
    </form>
  );
}
