import { WorkerEntrypoint } from "cloudflare:workers";
import { notifySuperAdmin, type BackupHealthNotification } from "./consumer-notifications";
import type { Env } from "./types";

export class ConsumerNotifications extends WorkerEntrypoint<Env> {
  async notifySuperAdmin(input: BackupHealthNotification) {
    return notifySuperAdmin(this.env, input);
  }
}
