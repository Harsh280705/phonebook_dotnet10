import { test, expect } from "@playwright/test";
import { BASE_URL, createContact, createTestUser, deleteContacts, login, searchFor } from "./helpers";

test.describe("Contact screen and CRUD", () => {
  test("contact screen shows the main controls and list", async ({ page }) => {
    const { api, credentials } = await createTestUser("screen");
    const contact = await createContact(api, { name: "Screen Contact" });
    await login(page, credentials);

    await expect(page.getByRole("button", { name: "Add contact" })).toBeVisible();
    await expect(page.getByPlaceholder("Search by name, phone number or email...")).toBeVisible();
    await expect(page.getByRole("button", { name: "Import CSV" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Export CSV" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Log out" })).toBeVisible();
    await expect(page.locator(".contact-card").filter({ hasText: contact.name })).toBeVisible();
    await expect(page.locator(".contact-count")).toContainText("1 people");

    await deleteContacts(api, [contact]);
  });

  test("creates a contact and shows it in the list", async ({ page }) => {
    const { api, credentials } = await createTestUser("create");
    await login(page, credentials);
    await page.getByRole("button", { name: "Add contact" }).click();

    await page.locator('input[autocomplete="name"]').fill("Created By Playwright");
    await page.locator('input[autocomplete="tel"]').fill("+14155550111");
    await page.locator('input[autocomplete="email"]').fill("created-by-playwright@example.com");
    await page.locator('textarea[autocomplete="street-address"]').fill("123 Created Street");
    await page.getByRole("button", { name: "Add contact", exact: true }).last().click();

    await searchFor(page, "Created By Playwright");
    await expect(page.locator(".contact-card").filter({ hasText: "+14155550111" })).toBeVisible();
    const contacts = await (await api.get(`${BASE_URL}/api/contacts/?page=1&limit=10`)).json();
    await deleteContacts(api, contacts.items);
  });

  test("shows existing client-side validation for invalid phone and email", async ({ page }) => {
    const { api, credentials } = await createTestUser("validation");
    await login(page, credentials);
    await page.getByRole("button", { name: "Add contact" }).click();
    await page.locator('input[autocomplete="name"]').fill("Validation Contact");
    await page.locator('input[autocomplete="tel"]').fill("123");
    await page.locator('input[autocomplete="email"]').fill("valid@example.com");
    await page.getByRole("button", { name: "Add contact", exact: true }).last().click();

    await expect(page.locator(".form-error")).toHaveText(/valid .*phone number/);

    await page.locator('input[autocomplete="tel"]').fill("+14155550987");
    await page.locator('input[autocomplete="email"]').fill("not-an-email");
    await expect(page.locator('input[autocomplete="email"]')).toHaveJSProperty("validity.valid", false);
    await api.dispose();
  });

  test("edits a dedicated contact", async ({ page }) => {
    const { api, credentials } = await createTestUser("edit");
    const contact = await createContact(api, { name: "Before Edit" });
    await login(page, credentials);
    await page.locator(".contact-card").filter({ hasText: "Before Edit" }).click();
    await page.getByRole("button", { name: "Edit contact" }).click();
    await page.locator('input[autocomplete="name"]').fill("After Edit");
    await page.getByRole("button", { name: "Save changes" }).click();
    await searchFor(page, "After Edit");
    await expect(page.locator(".contact-card").filter({ hasText: "Before Edit" })).toHaveCount(0);
    await deleteContacts(api, [{ id: contact.id }]);
  });

  test("deletes a dedicated contact after confirmation", async ({ page }) => {
    const { api, credentials } = await createTestUser("delete");
    const contact = await createContact(api, { name: "Delete Me" });
    await login(page, credentials);
    await page.locator(".contact-card").filter({ hasText: "Delete Me" }).click();
    page.once("dialog", (dialog) => dialog.accept());
    await page.getByRole("button", { name: "Delete" }).click();
    await expect(page.locator(".contact-card").filter({ hasText: "Delete Me" })).toHaveCount(0);
    await api.dispose();
  });

  test("search filters results and clearing restores the list", async ({ page }) => {
    const { api, credentials } = await createTestUser("search");
    const match = await createContact(api, { name: "Searchable Person" });
    const other = await createContact(api, { name: "Different Person" });
    await login(page, credentials);
    await searchFor(page, "Searchable Person");
    await expect(page.locator(".contact-card").filter({ hasText: "Different Person" })).toHaveCount(0);
    await page.locator(".clear-search").click();
    await expect(page.locator(".contact-card").filter({ hasText: "Different Person" })).toBeVisible();
    await deleteContacts(api, [match, other]);
  });
});
