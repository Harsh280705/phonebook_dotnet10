import { expect, request } from "@playwright/test";

export const BASE_URL = process.env.BASE_URL || "http://localhost";

export async function createTestUser(prefix = "pwuser") {
  const api = await request.newContext({ baseURL: BASE_URL });
  const suffix = `${Date.now()}_${Math.random().toString(36).slice(2, 8)}`;
  const credentials = {
    username: `${prefix}_${suffix}`,
    email: `${prefix}_${suffix}@example.com`,
    password: "StrongPass123!"
  };

  const response = await api.post("/api/auth/register", {
    data: credentials
  });
  expect(response.ok()).toBeTruthy();

  return { api, credentials };
}

export async function login(page, credentials) {
  await page.goto("/");
  await page.getByLabel("Username or email").fill(credentials.username);
  await page.getByLabel("Password").fill(credentials.password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Phonebook", exact: true })).toBeVisible();
}

export async function createContact(api, overrides = {}) {
  const suffix = `${Date.now()}_${Math.random().toString(36).slice(2, 7)}`;
  const contact = {
    name: `Playwright Contact ${suffix}`,
    phone_number: `+1415${String(Math.floor(Math.random() * 1_000_0000)).padStart(7, "0")}`,
    email: `contact_${suffix}@example.com`,
    address: "123 Playwright Test Street",
    ...overrides
  };

  const response = await api.post("/api/contacts/", {
    data: contact
  });
  if (!response.ok()) {
    throw new Error(`Contact setup failed: ${response.status()} ${await response.text()}`);
  }
  expect(response.ok()).toBeTruthy();
  return response.json();
}

export async function deleteContacts(api, contacts) {
  for (const contact of contacts) {
    await api.delete(`http://localhost/api/contacts/${contact.id}`);
  }
  await api.dispose();
}

export async function searchFor(page, value) {
  const search = page.getByPlaceholder("Search by name, phone number or email...");
  await search.fill(value);
  await expect(page.locator(".contact-card").filter({ hasText: value }).first()).toBeVisible();
}
