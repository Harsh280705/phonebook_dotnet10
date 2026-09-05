import { test, expect } from "@playwright/test";
import { createContact, createTestUser, deleteContacts, login } from "./helpers";

test.describe("CSV import and export", () => {
  test("imports a valid CSV, reports duplicates, and refreshes the list", async ({ page }) => {
    const { api, credentials } = await createTestUser("import");
    await login(page, credentials);

    const csv = [
      "name,phone_number,email,address",
      "Imported Alpha,+14155550221,imported-alpha@example.com,123 Import Street",
      "Imported Beta,+14155550222,imported-beta@example.com,456 Import Avenue"
    ].join("\n");
    const file = {
      name: "contacts.csv",
      mimeType: "text/csv",
      buffer: Buffer.from(csv)
    };

    await page.locator('input[type="file"]').setInputFiles(file);
    await expect(page.locator(".import-summary")).toContainText("Imported: 2");
    await expect(page.locator(".contact-card").filter({ hasText: "Imported Alpha" })).toBeVisible();

    await page.locator('input[type="file"]').setInputFiles(file);
    await expect(page.locator(".import-summary")).toContainText("Duplicates skipped: 2");

    const response = await api.get("http://localhost/api/contacts/?page=1&limit=10&search=Imported");
    const imported = (await response.json()).items;
    await deleteContacts(api, imported);
  });

  test("export downloads a CSV with headers and contact data", async ({ page }) => {
    const { api, credentials } = await createTestUser("export");
    const contact = await createContact(api, {
      name: "Exported Playwright Contact",
      phone_number: "+14155550333",
      email: "exported-playwright@example.com"
    });
    await login(page, credentials);

    const downloadPromise = page.waitForEvent("download");
    await page.getByRole("button", { name: "Export CSV" }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toBe("phonebook.csv");
    const stream = await download.createReadStream();
    let content = "";
    for await (const chunk of stream) content += chunk.toString();

    expect(content).toContain("Name,Phone number,Email,Address");
    expect(content).toContain("Exported Playwright Contact");
    expect(content).toContain("exported-playwright@example.com");
    expect(content).toContain("+14155550333");

    await deleteContacts(api, [contact]);
  });
});
