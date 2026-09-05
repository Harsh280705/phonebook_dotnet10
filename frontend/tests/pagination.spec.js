import { test, expect } from "@playwright/test";
import { createContact, createTestUser, deleteContacts, login } from "./helpers";

test("numbered pagination keeps totals stable and supports ellipses, next, and previous", async ({ page }) => {
  const { api, credentials } = await createTestUser("pagination");
  const contacts = [];
  const phoneSeed = Date.now() % 10000000;

  for (let index = 0; index < 85; index += 1) {
    contacts.push(await createContact(api, {
      name: `Pagination Person ${String.fromCharCode(65 + (index % 26))} Contact`,
      phone_number: `+1415${String(phoneSeed + index).padStart(7, "0")}`,
      email: `pagination_${index}_${Date.now()}@example.com`
    }));
  }

  await login(page, credentials);
  await expect(page.locator(".contact-count")).toContainText("85 people");
  await expect(page.locator(".page-number").filter({ hasText: "..." })).toBeVisible();
  await expect(page.locator(".page-number.active")).toHaveText("1");
  await expect(page.getByRole("button", { name: "Previous" })).toBeDisabled();

  await page.locator(".page-number").filter({ hasText: "2" }).click();
  await expect(page.locator(".load-more-wrapper")).toContainText("Page 2 of 9");
  await expect(page.locator(".contact-count")).toContainText("85 people");
  await expect(page.locator(".page-number.active")).toHaveText("2");

  await page.getByRole("button", { name: "Next" }).click();
  await expect(page.locator(".load-more-wrapper")).toContainText("Page 3 of 9");
  await page.getByRole("button", { name: "Previous" }).click();
  await expect(page.locator(".load-more-wrapper")).toContainText("Page 2 of 9");

  await deleteContacts(api, contacts);
});
