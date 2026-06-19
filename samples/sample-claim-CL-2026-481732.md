# InsurTech — First Notice of Loss (FNOL) / Claim Document

> **Cognizant Confidential** · InsurTech Digital Insurance Claims & Policy Management Platform
> Sample artifact for demo / testing. Fields map 1:1 to the Claims service data model
> (`Claim` aggregate) and the `POST /v1/claims` request contract.

---

## 1. Claim Summary

| Field | Value |
|---|---|
| **Claim Number** | CL-2026-481732 |
| **Claim Type** | MotorOwnDamage |
| **Status** | Filed |
| **Filed At** | 2026-06-18T09:42:11Z |
| **Channel** | CustomerPortal |
| **Filed By (User Id)** | usr-7c41a9e2 |
| **Currency** | INR |
| **Claimed (Estimated) Amount** | ₹ 86,500.00 |

## 2. Policy & Policyholder

| Field | Value |
|---|---|
| **Policy Id** | POL-2025-220148 |
| **Policy Holder** | Ananya Sharma |
| **Vehicle** | 2022 Hyundai Creta SX (Petrol) |
| **Registration No.** | KA-05-MH-4821 |
| **Insured Declared Value (IDV)** | ₹ 13,40,000.00 |
| **Policy Period** | 2025-11-01 to 2026-10-31 |

## 3. Incident Details

| Field | Value |
|---|---|
| **Incident Date** | 2026-06-15T18:20:00Z |
| **Incident Type** | Collision (third-party, no injuries) |
| **Location (Address)** | 14th Main Rd, HSR Layout Sector 6, Bengaluru, Karnataka 560102 |
| **Geo (Lat, Lng)** | 12.9121, 77.6446 |
| **Police FIR Filed** | Yes — FIR No. 0342/2026, HSR Layout PS |

**Description of Loss**

> While stationary at the HSR Layout Sector 6 signal at approximately 18:20 on
> 15 Jun 2026, the insured vehicle was struck from the rear by a delivery van. Impact
> damaged the rear bumper, tail-light assembly, and boot lid. No personal injuries
> reported. Third-party driver acknowledged fault at the scene; details exchanged and
> FIR filed at HSR Layout Police Station.

## 4. Attached Documents

These attachments are uploaded to the Documents service and flow through the malware
scan + OCR/Document-Intelligence extraction pipeline before the claim is triaged.

| Document Id | Type | Description |
|---|---|---|
| DOC-9f2a14c0 | RepairEstimate | Authorised garage estimate (see §5) |
| DOC-1b7e63d5 | PhotoEvidence | 4 photos of rear-end damage |
| DOC-44c8a90e | PoliceReport | FIR 0342/2026 scan |
| DOC-58d0f7b2 | DrivingLicense | Policyholder DL (KYC) |

## 5. Supporting Repair Estimate (source for OCR extraction)

```
SPEEDLINE AUTOWORKS — Authorised Hyundai Service Centre
GSTIN: 29ABCDE1234F1Z5      Estimate #: EST-2026-5571      Date: 2026-06-17

Vehicle: Hyundai Creta SX (2022)        Reg: KA-05-MH-4821
--------------------------------------------------------------------
Line Item                                  Qty      Amount (INR)
--------------------------------------------------------------------
Rear bumper assembly (OEM)                  1         24,800.00
Tail-light assembly (RH)                    1         11,200.00
Boot lid panel + repaint                    1         28,500.00
Sensor recalibration                        1          4,000.00
Labour (12 hrs @ ₹650)                      -          7,800.00
--------------------------------------------------------------------
                                  Subtotal             76,300.00
                                  GST @ 18%            13,734.00
--------------------------------------------------------------------
                                  TOTAL                90,034.00
                                  Less policy excess   (3,534.00)
                                  CLAIMABLE            86,500.00
--------------------------------------------------------------------
```

**Expected OCR-extracted fields** (surfaced on the Claim Details screen):

| Extracted Field | Value |
|---|---|
| `vendorName` | Speedline Autoworks |
| `documentNumber` | EST-2026-5571 |
| `documentDate` | 2026-06-17 |
| `totalAmount` | 90034.00 |
| `claimableAmount` | 86500.00 |
| `currency` | INR |

## 6. Lifecycle (expected status history)

`Filed` → `Triaged` (fraud score + documents verified) → `Approved` → `Paid`

---

## Appendix A — `POST /v1/claims` request payload

Matches `FileClaimRequest` in
`backend/src/Services/Claims/Claims.Application/Contracts/ClaimContracts.cs`.

```json
{
  "policyId": "POL-2025-220148",
  "claimType": "MotorOwnDamage",
  "incidentDate": "2026-06-15T18:20:00Z",
  "incidentLocation": {
    "lat": 12.9121,
    "lng": 77.6446,
    "address": "14th Main Rd, HSR Layout Sector 6, Bengaluru, Karnataka 560102"
  },
  "description": "Insured vehicle struck from rear by a delivery van while stationary at the HSR Layout Sector 6 signal. Rear bumper, tail-light assembly and boot lid damaged. No injuries. FIR 0342/2026 filed.",
  "estimatedAmount": 86500.00,
  "currency": "INR",
  "attachments": [
    { "documentId": "DOC-9f2a14c0", "type": "RepairEstimate" },
    { "documentId": "DOC-1b7e63d5", "type": "PhotoEvidence" },
    { "documentId": "DOC-44c8a90e", "type": "PoliceReport" },
    { "documentId": "DOC-58d0f7b2", "type": "DrivingLicense" }
  ],
  "filedBy": {
    "userId": "usr-7c41a9e2",
    "channel": "CustomerPortal"
  }
}
```

## Appendix B — expected `GET /v1/claims/{id}` response (post-triage)

```json
{
  "claimId": "3f1a9c84-2d77-4e0b-9a16-7c8e5b0d4412",
  "claimNumber": "CL-2026-481732",
  "policyId": "POL-2025-220148",
  "claimType": "MotorOwnDamage",
  "status": "Triaged",
  "claimedAmount": 86500.00,
  "approvedAmount": null,
  "currency": "INR",
  "fraudScore": 0.18,
  "fraudDecision": "allow",
  "documentsVerified": true,
  "decisionReason": null,
  "filedAt": "2026-06-18T09:42:11Z",
  "incidentDate": "2026-06-15T18:20:00Z",
  "documents": [
    { "documentId": "DOC-9f2a14c0", "type": "RepairEstimate", "verified": true },
    { "documentId": "DOC-1b7e63d5", "type": "PhotoEvidence", "verified": true },
    { "documentId": "DOC-44c8a90e", "type": "PoliceReport", "verified": true },
    { "documentId": "DOC-58d0f7b2", "type": "DrivingLicense", "verified": true }
  ],
  "history": [
    { "status": "Filed", "note": "Claim filed (FNOL received)", "timestamp": "2026-06-18T09:42:11Z" },
    { "status": "Triaged", "note": "Fraud triage: score=0.18, decision=allow", "timestamp": "2026-06-18T09:43:02Z" }
  ]
}
```

---
*InsurTech Architecture Office · Cognizant Confidential*
