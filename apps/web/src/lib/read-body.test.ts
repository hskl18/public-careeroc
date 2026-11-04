import { describe, expect, it } from "vitest";
import { readBodyWithin } from "./read-body";

describe("readBodyWithin", () => {
  it("accepts a bodyless DELETE, which browsers send without Content-Length", async () => {
    const body = await readBodyWithin(new Request("http://x/", { method: "DELETE" }), 10);
    expect(body?.byteLength).toBe(0);
  });

  it("returns the body up to the limit and null past it", async () => {
    const post = (text: string) => new Request("http://x/", { method: "POST", body: text });
    expect((await readBodyWithin(post("0123456789"), 10))?.toString()).toBe("0123456789");
    expect(await readBodyWithin(post("0123456789x"), 10)).toBeNull();
  });
});
