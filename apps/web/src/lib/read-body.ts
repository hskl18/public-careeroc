// Reads a request body up to `limit` bytes and returns null past it. Browsers send DELETE without
// Content-Length and chunked bodies have none, so the cap is enforced while reading.
export async function readBodyWithin(
  request: Request,
  limit: number,
): Promise<Buffer<ArrayBuffer> | null> {
  if (!request.body) return Buffer.alloc(0);

  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let size = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) {
      const body = Buffer.alloc(size);
      let offset = 0;
      for (const chunk of chunks) {
        body.set(chunk, offset);
        offset += chunk.byteLength;
      }
      return body;
    }
    size += value.byteLength;
    if (size > limit) {
      await reader.cancel();
      return null;
    }
    chunks.push(value);
  }
}
