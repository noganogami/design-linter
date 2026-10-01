# Design Document

## Boundary Commitments

### This Spec Owns
Payment recording and retry execution.

### Out of Boundary
Order fulfilment and billing reconciliation.

### Allowed Dependencies
The payment service may use the external payment gateway.

### Revalidation Triggers
Payment contract or retry policy changes.

## File Structure Plan

| Path | Component | Responsibility |
|---|---|---|
| src/payment/service.ts | PaymentService | Record payment results |
| src/payment/retry.ts | PaymentRetryWorker | Execute retries |

## Components and Interfaces

| Component | Domain/Layer | Intent | Req Coverage | Key Dependencies (P0/P1) | Contracts |
|---|---|---|---|---|---|
| PaymentService | Service | Record payments | 1.1 | Gateway (P0) | API |
| PaymentRetryWorker | Runtime | Execute retries | 1.2 | PaymentService (P0) | Batch |

### Payment

#### PaymentService

| Field | Detail |
|---|---|
| Intent | Record a payment result |
| Requirements | 1.1 |

**Dependencies**
- Inbound: PaymentRetryWorker — retry request (P0)
- External: Gateway — payment processing (P0)

#### PaymentRetryWorker

| Field | Detail |
|---|---|
| Intent | Execute the retry policy |
| Requirements | 1.2 |

**Dependencies**
- Outbound: PaymentService — retry payment (P0)

##### Service Interface
Retry API exposes PaymentRetryWorker retry operations.

## Requirements Traceability
| Requirement | Interfaces |
|---|---|
| 1.2 | Retry API |
