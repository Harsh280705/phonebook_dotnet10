import { test, expect } from "@playwright/test";
import { BASE_URL, createContact, createTestUser, deleteContacts, login, searchFor } from "./helpers";

test.describe("Contact tags", () => {
  test("creates tags, assigns them to contacts, and filters with search", async ({ page }) => {
    const { api, credentials } = await createTestUser("tags");
    await login(page, credentials);

    await page.getByRole("button", { name: "Tags" }).click();
    await page.getByPlaceholder("e.g. Work").fill("Work");
    await page.getByRole("button", { name: "Create tag" }).click();
    await expect(page.locator(".tag-manager-list")).toContainText("Work");
    await page.getByPlaceholder("e.g. Work").fill("Family");
    await page.getByRole("button", { name: "Create tag" }).click();
    await expect(page.locator(".tag-manager-list")).toContainText("Family");
    await page.locator(".tag-manager .form-close").click();

    await page.getByRole("button", { name: "Add contact" }).click();
    await page.locator('input[autocomplete="name"]').fill("Tagged Alice");
    await page.locator('input[autocomplete="tel"]').fill("+14155553001");
    await page.locator('input[autocomplete="email"]').fill("tagged-alice@example.com");
    await page.locator('textarea[autocomplete="street-address"]').fill("123 Tagged Street");
    await page.locator(".tag-option").filter({ hasText: "Work" }).click();
    await page.locator(".tag-option").filter({ hasText: "Family" }).click();
    await page.getByRole("button", { name: "Add contact", exact: true }).last().click();

    const other = await createContact(api, { name: "Untagged Bob" });
    await page.reload();
    await expect(page.locator(".contact-card").filter({ hasText: "Untagged Bob" })).toBeVisible();

    await searchFor(page, "Tagged Alice");
    await expect(page.locator(".contact-card").filter({ hasText: "Tagged Alice" }).locator(".tag-pill")).toHaveCount(2);

    await page.locator(".clear-search").click();
    await page.locator(".tag-filter").filter({ hasText: "Work" }).click();
    await expect(page.locator(".contact-card").filter({ hasText: "Tagged Alice" })).toBeVisible();
    await expect(page.locator(".contact-card").filter({ hasText: "Untagged Bob" })).toHaveCount(0);

    await searchFor(page, "Tagged Alice");
    await expect(page.locator(".contact-card").filter({ hasText: "Tagged Alice" })).toBeVisible();

    const contacts = await (await api.get(`${BASE_URL}/api/contacts/?page=1&limit=10`)).json();
    await deleteContacts(api, [...contacts.items, other]);
  });
});
