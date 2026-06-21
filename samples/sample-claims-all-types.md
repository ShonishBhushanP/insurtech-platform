# InsurTech — Sample Claims (one per claim type)

> **Cognizant Confidential** · InsurTech Digital Insurance Claims & Policy Management Platform
> One sample claim for each `ClaimType` enum value — `Motor`, `Health`, `Property`, `Travel`, `Life`
> (`Claims.Domain/Enums.cs`). Each payload matches `FileClaimRequest`
> (`Claims.Application/Contracts/ClaimContracts.cs`) and can be POSTed to `/v1/claims`.

| # | Claim Number | Type | Claimed (INR) | Scenario |
|---|---|---|---|---|
| 1 | CL-2026-481732 | Motor | ₹ 86,500 | Rear-end collision, own-damage |
| 2 | CL-2026-503914 | Health | ₹ 1,42,300 | Cashless hospitalisation (appendectomy) |
| 3 | CL-2026-517066 | Property | ₹ 3,75,000 | Home fire damage (kitchen) |
| 4 | CL-2026-528471 | Travel | ₹ 64,200 | Trip cancellation + baggage loss |
| 5 | CL-2026-539885 | Life | ₹ 25,00,000 | Term life death-benefit claim |

---

## 1. Motor — CL-2026-481732

| Field | Value |
|---|---|
| **Policy Id** | POL-2025-220148 |
| **Policyholder** | Ananya Sharma |
| **Incident Date** | 2026-06-15T18:20:00Z |
| **Location** | 14th Main Rd, HSR Layout Sector 6, Bengaluru, KA 560102 |
| **Claimed Amount** | ₹ 86,500.00 |
| **Attachments** | RepairEstimate, PhotoEvidence, PoliceReport, DrivingLicense |

> Insured 2022 Hyundai Creta (KA-05-MH-4821) struck from the rear while stationary at a
> signal. Rear bumper, tail-light and boot lid damaged; no injuries. FIR 0342/2026 filed.

```json
{
  "policyId": "POL-2025-220148",
  "claimType": "Motor",
  "incidentDate": "2026-06-15T18:20:00Z",
  "incidentLocation": { "lat": 12.9121, "lng": 77.6446, "address": "14th Main Rd, HSR Layout Sector 6, Bengaluru, Karnataka 560102" },
  "description": "Insured vehicle struck from rear by a delivery van while stationary at the HSR Layout Sector 6 signal. Rear bumper, tail-light assembly and boot lid damaged. No injuries. FIR 0342/2026 filed.",
  "estimatedAmount": 86500.00,
  "currency": "INR",
  "attachments": [
    { "documentId": "DOC-9f2a14c0", "type": "RepairEstimate" },
    { "documentId": "DOC-1b7e63d5", "type": "PhotoEvidence" },
    { "documentId": "DOC-44c8a90e", "type": "PoliceReport" },
    { "documentId": "DOC-58d0f7b2", "type": "DrivingLicense" }
  ],
  "filedBy": { "userId": "usr-7c41a9e2", "channel": "CustomerPortal" }
}
```

---

## 2. Health — CL-2026-503914

| Field | Value |
|---|---|
| **Policy Id** | POL-2024-118702 |
| **Policyholder** | Rohan Mehta |
| **Incident Date** | 2026-06-09T07:15:00Z |
| **Location** | Manipal Hospital, Old Airport Rd, Bengaluru, KA 560017 |
| **Claimed Amount** | ₹ 1,42,300.00 |
| **Attachments** | DischargeSummary, HospitalBill, DiagnosticReport, PolicyCard |

> Insured admitted with acute appendicitis; laparoscopic appendectomy performed. 3-day
> cashless hospitalisation. Claim covers room, surgery, anaesthesia, and pharmacy.

```json
{
  "policyId": "POL-2024-118702",
  "claimType": "Health",
  "incidentDate": "2026-06-09T07:15:00Z",
  "incidentLocation": { "lat": 12.9606, "lng": 77.6486, "address": "Manipal Hospital, Old Airport Rd, Bengaluru, Karnataka 560017" },
  "description": "Insured admitted with acute appendicitis. Laparoscopic appendectomy performed; 3-day cashless hospitalisation. Claim for room charges, surgery, anaesthesia and pharmacy.",
  "estimatedAmount": 142300.00,
  "currency": "INR",
  "attachments": [
    { "documentId": "DOC-77a1c2e9", "type": "DischargeSummary" },
    { "documentId": "DOC-90b4f1aa", "type": "HospitalBill" },
    { "documentId": "DOC-2c6d8e34", "type": "DiagnosticReport" },
    { "documentId": "DOC-13ff0a57", "type": "PolicyCard" }
  ],
  "filedBy": { "userId": "usr-3a98b1d4", "channel": "CustomerPortal" }
}
```

---

## 3. Property — CL-2026-517066

| Field | Value |
|---|---|
| **Policy Id** | POL-2025-301556 |
| **Policyholder** | Kavya Iyer |
| **Incident Date** | 2026-06-11T21:40:00Z |
| **Location** | Flat 1204, Prestige Lakeside, Whitefield, Bengaluru, KA 560066 |
| **Claimed Amount** | ₹ 3,75,000.00 |
| **Attachments** | FireBrigadeReport, DamagePhotos, RepairQuote, OwnershipProof |

> Electrical short circuit in the kitchen caused a fire damaging cabinetry, appliances and
> false ceiling. Fire brigade attended; no injuries. Surveyor inspection scheduled.

```json
{
  "policyId": "POL-2025-301556",
  "claimType": "Property",
  "incidentDate": "2026-06-11T21:40:00Z",
  "incidentLocation": { "lat": 12.9698, "lng": 77.7499, "address": "Flat 1204, Prestige Lakeside, Whitefield, Bengaluru, Karnataka 560066" },
  "description": "Electrical short circuit in the kitchen caused a fire damaging modular cabinetry, appliances and false ceiling. Fire brigade attended (report attached); no injuries. Surveyor inspection scheduled.",
  "estimatedAmount": 375000.00,
  "currency": "INR",
  "attachments": [
    { "documentId": "DOC-5e2901bc", "type": "FireBrigadeReport" },
    { "documentId": "DOC-8aa31d70", "type": "DamagePhotos" },
    { "documentId": "DOC-c41b9e02", "type": "RepairQuote" },
    { "documentId": "DOC-6f7d5a18", "type": "OwnershipProof" }
  ],
  "filedBy": { "userId": "usr-5d22c8f1", "channel": "CustomerPortal" }
}
```

---

## 4. Travel — CL-2026-528471

| Field | Value |
|---|---|
| **Policy Id** | POL-2026-410233 |
| **Policyholder** | Arjun Nair |
| **Incident Date** | 2026-06-05T03:30:00Z |
| **Location** | Kempegowda Intl Airport (BLR), Devanahalli, Bengaluru, KA 560300 |
| **Claimed Amount** | ₹ 64,200.00 |
| **Attachments** | AirlinePIR, BoardingPass, CancellationProof, ExpenseReceipts |

> Insured's international flight cancelled by the airline; onward connection and pre-paid
> hotel forfeited. One checked bag delivered damaged. Claim covers cancellation + baggage.

```json
{
  "policyId": "POL-2026-410233",
  "claimType": "Travel",
  "incidentDate": "2026-06-05T03:30:00Z",
  "incidentLocation": { "lat": 13.1986, "lng": 77.7066, "address": "Kempegowda International Airport (BLR), Devanahalli, Bengaluru, Karnataka 560300" },
  "description": "Airline cancelled the insured's international flight; onward connection missed and pre-paid hotel forfeited. One checked bag delivered damaged. Claim for trip cancellation and baggage loss.",
  "estimatedAmount": 64200.00,
  "currency": "INR",
  "attachments": [
    { "documentId": "DOC-a1f4cc90", "type": "AirlinePIR" },
    { "documentId": "DOC-b73e2210", "type": "BoardingPass" },
    { "documentId": "DOC-ce0918f4", "type": "CancellationProof" },
    { "documentId": "DOC-22d6e8a3", "type": "ExpenseReceipts" }
  ],
  "filedBy": { "userId": "usr-9b41e0c7", "channel": "CustomerPortal" }
}
```

---

## 5. Life — CL-2026-539885

| Field | Value |
|---|---|
| **Policy Id** | POL-2019-002871 |
| **Life Assured** | Suresh Kumar |
| **Nominee (Claimant)** | Lakshmi Kumar |
| **Incident Date** | 2026-05-28T00:00:00Z |
| **Location** | Chennai, Tamil Nadu 600040 |
| **Claimed Amount (Sum Assured)** | ₹ 25,00,000.00 |
| **Attachments** | DeathCertificate, NomineeIdProof, PolicyBond, BankMandate |

> Death-benefit claim filed by the registered nominee on the term life policy. Death due to
> natural causes; municipal death certificate attached. Claim is for the full sum assured.

```json
{
  "policyId": "POL-2019-002871",
  "claimType": "Life",
  "incidentDate": "2026-05-28T00:00:00Z",
  "incidentLocation": { "lat": 13.0827, "lng": 80.2707, "address": "Chennai, Tamil Nadu 600040" },
  "description": "Death-benefit claim filed by the registered nominee on a term life policy. Death due to natural causes; municipal death certificate attached. Claim for the full sum assured.",
  "estimatedAmount": 2500000.00,
  "currency": "INR",
  "attachments": [
    { "documentId": "DOC-d0e1a294", "type": "DeathCertificate" },
    { "documentId": "DOC-4b88f0c1", "type": "NomineeIdProof" },
    { "documentId": "DOC-71ac3e60", "type": "PolicyBond" },
    { "documentId": "DOC-9c2f5b08", "type": "BankMandate" }
  ],
  "filedBy": { "userId": "usr-6e0f7a92", "channel": "CustomerPortal" }
}
```

---
*InsurTech Architecture Office · Cognizant Confidential*
