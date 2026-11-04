import { useSyncExternalStore } from "react";

function subscribe() {
  return () => {};
}

// False during the server render and hydration, true afterwards. Values only the browser knows,
// such as the viewer's time zone or the current time, render after hydration instead of
// mismatching the server's HTML.
export function useHydrated() {
  return useSyncExternalStore(
    subscribe,
    () => true,
    () => false,
  );
}
