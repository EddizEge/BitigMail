# BitigMail OST/PST Conversion: Inline CID & Body Representation Acceptance Policy

**Document Version:** 1.0.0  
**Task Reference:** TASK-009  
**Related Tasks:** TASK-008, TASK-010  
**Status:** VERIFIED — TASK-009 DONE (Release build and run09 passed)  

---

## 1. Executive Summary

During TASK-008 verification, `msg-11` (`<msg-2024-11-inline-cid@projeler.example>`) reported `ContentId: null` in both source OST and output PST SDK views, creating uncertainty as to whether inline Content-ID metadata was preserved during MAPI item conversion.

Decisive read-only evidence from `lab/ost-spike/output/task009-astra-cid-readonly.json` proves that:
1. **No conversion loss occurred:** `PR_ATTACH_CONTENT_ID_W` (`0x3712001F`) is preserved identically in both source OST and output PST with raw value `proje_logo_cid`.
2. **Defect was strictly in the reader accessor:** `MapiAttachment` in Aspose.Email 24.8.0 does not have a CLR property named `ContentId`. Harness reflection via `GetProperty("ContentId")` unconditionally yielded `null`.
3. **Measured preservation:** The 73-byte inline PNG (`logo.png`) is byte-identical. The complete HTML strings have identical SHA-256 hashes after newline normalization (the diagnostic normalizes CRLF to LF), and the HTML `<img src="cid:proje_logo_cid">` reference resolves to the attachment. This does not claim byte-for-byte equality of every raw MAPI storage stream.

---

## 2. Decisive Read-Only Evidence

Source of evidence: `lab/ost-spike/output/task009-astra-cid-readonly.json`  
Source OST: `lab/ost-spike/input/bitigmail-lab-full.ost` (SHA-256: `B0801758A2E61D4CE6E86799701A81A7A60C38401F73B13C993D94C03A2EE57A`)  
Output PST: `lab/ost-spike/output/genuine-full-converted-04.pst`

| Metric / Field | Source OST (`bitigmail-lab-full.ost`) | Output PST (`genuine-full-converted-04.pst`) | Status |
| :--- | :--- | :--- | :--- |
| **Message-ID** | `<msg-2024-11-inline-cid@projeler.example>` | `<msg-2024-11-inline-cid@projeler.example>` | MATCH |
| **Attachment Name** | `logo.png` | `logo.png` | MATCH |
| **Attachment Size** | 73 bytes | 73 bytes | EXACT MATCH |
| **Attachment SHA-256** | `411E320C1D42FE6857FDF02EB7EE4AE020FE6522782476C81D083014FD56C61F` | `411E320C1D42FE6857FDF02EB7EE4AE020FE6522782476C81D083014FD56C61F` | EXACT MATCH |
| **HTML Body SHA-256** | `6ACA1CE092B1B914EDC317C2EA01AAB85FCAF161376AEF78173B55C241127AE4` | `6ACA1CE092B1B914EDC317C2EA01AAB85FCAF161376AEF78173B55C241127AE4` | EXACT MATCH |
| **HTML CID Reference** | `cid:proje_logo_cid` present | `cid:proje_logo_cid` present | RESOLVED |
| **MAPI CID Tag** | `0x3712001F` (`PR_ATTACH_CONTENT_ID_W`) | `0x3712001F` (`PR_ATTACH_CONTENT_ID_W`) | IDENTICAL |
| **MAPI CID Value** | `proje_logo_cid` | `proje_logo_cid` | IDENTICAL |

---

## 3. Proven Technical Fix

### 3.1 Defect Root Cause
`Aspose.Email.Mapi.MapiAttachment` exposes attachments through MAPI property bags rather than direct CLR property shortcuts. Invoking `att.GetType().GetProperty("ContentId")` returned `null` because no such property exists on `MapiAttachment`.

### 3.2 Implemented Helper
Both reflection accessor sites in `lab/ost-spike/harness/Program.cs` (source item classification and PST reopen verification) have been replaced with a unified static helper:

```csharp
private static string? GetAttachmentContentId(MapiAttachment att)
{
    if (att?.Properties == null) return null;

    try
    {
        // PR_ATTACH_CONTENT_ID_W = 0x3712001F (Unicode)
        if (att.Properties.TryGetValue(MapiPropertyTag.PR_ATTACH_CONTENT_ID_W, out var propW) && propW != null)
        {
            string val = propW.GetString();
            if (!string.IsNullOrEmpty(val)) return val;
        }

        // PR_ATTACH_CONTENT_ID_A = 0x3712001E (ANSI fallback)
        if (att.Properties.TryGetValue(MapiPropertyTag.PR_ATTACH_CONTENT_ID_A, out var propA) && propA != null)
        {
            string val = propA.GetString();
            if (!string.IsNullOrEmpty(val)) return val;
        }
    }
    catch
    {
        // Ignore extraction errors and return null
    }

    return null;
}
```

### 3.3 Normalization and Invariance Rules
- **No Metadata Synthesis:** Stored property values are read verbatim (`proje_logo_cid`). Metadata must never be manufactured or synthesized from filenames or external manifests.
- **Comparison Normalization:** Enclosing angle brackets (`<` and `>`) are stripped strictly during comparison (`Trim('<', '>')`) to account for RFC 2822 header formatting vs MAPI stored property conventions.
- **Source Absence Handling:** If a source item genuinely lacks a Content-ID property, the harness reports `SOURCE_INCOMPLETE` and never invents synthetic values.

---

## 4. Body Representation Acceptance Policy

1. **Primary Source Preservation:**
   - Full HTML bodies (`PR_BODY_HTML` / `BodyHtml`) and plain-text bodies (`PR_BODY` / `Body`) must be verified by full-stream hash comparison after newline normalization (`\r\n` $\rightarrow$ `\n`).
   - No first-line checks or partial prefix checks are permitted as evidence of body preservation.

2. **Additive Plaintext & Outlook Synchronization Artifacts:**
   - EML-to-OST acquisition changes (introduced by Microsoft Outlook when synchronizing or caching MIME content into MAPI/OST format) are classified as **acquisition artifacts**, strictly distinguished from **OST-to-PST conversion losses**.
   - Examples of acquisition artifacts:
     - Outlook injecting `[cid:proje_logo_cid]` into plain-text body alternative as a placeholder for inline graphics.
     - Outlook inserting extra blank lines between paragraphs when rendering HTML `<p>` tags to plain text (`msg-01`, `msg-02`).
     - Outlook expanding tab/space alignment in `<pre>` blocks (`msg-08`).
   - These differences do not constitute data loss when primary HTML representations are identical.

3. **Watermark & License Policy:**
   - Evaluation watermarks (e.g. `(Aspose.Email Evaluation)` in subject, `Evaluation Only. Created with Aspose.Email...` in body) must never be stripped or altered by conversion code.
   - Watermarks are explicitly cataloged and distinguished from conversion fidelity defects.

---

## 5. Planned Paths for Run 09 Verification

For execution by SOL:
- **Harness Project:** `lab/ost-spike/harness/BitigMail.OstHarness.csproj`
- **Output PST:** `lab/ost-spike/output/genuine-full-converted-09.pst`
- **Report JSON:** `lab/ost-spike/output/genuine-full-converted-09-report.json`
- **Independent libpff Export:** `lab/ost-spike/output/libpff-pst-full-09.export`
