import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';

/**
 * Waits for an email the API wrote to the pickup folder (Development email transport) and returns its link. Emails are
 * sent by the background outbox worker, so they arrive a few seconds after the request that queued them.
 */
export async function latestLinkFor(
  mailDirectory: string,
  email: string,
  kind: 'set-password' | 'reset-password',
  timeoutMs = 20_000,
): Promise<string> {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const link = findLink(mailDirectory, email, kind);
    if (link) return link;
    if (Date.now() > deadline) throw new Error(`No ${kind} email found for ${email} in ${mailDirectory}.`);
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
}

function findLink(mailDirectory: string, email: string, kind: string): string | undefined {
  const files = readdirSync(mailDirectory)
    .filter((name) => name.endsWith('.eml'))
    .sort()
    .reverse();
  for (const file of files) {
    const message = decodeQuotedPrintable(readFileSync(path.join(mailDirectory, file), 'utf8'));
    if (!message.toLowerCase().includes(email.toLowerCase())) continue;
    const match = message.match(new RegExp(`(https?://[^\\s"<>]+/${kind}\\?token=[A-Za-z0-9_%-]+)`));
    if (match) return match[1];
  }
  return undefined;
}

function decodeQuotedPrintable(text: string): string {
  return text.replace(/=\r?\n/g, '').replace(/=([0-9A-F]{2})/g, (_, hex: string) => String.fromCharCode(parseInt(hex, 16)));
}
