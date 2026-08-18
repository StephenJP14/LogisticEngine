# Logistics Engine API Documentation

This project exposes a logistics and delivery management API for managing packages, manifests, fleet telemetry, users, vehicles, and delivery evidence.

The API is built with ASP.NET Core and uses JWT authentication, PostgreSQL, Redis, MinIO/S3 storage, and SignalR for fleet updates.

## 1. Base URL

- Local development: http://localhost:5172
- If the app is running with a different port, replace accordingly.

## 2. Response Format

All API responses follow a standard envelope:

```json
{
  "status": 200,
  "success": true,
  "message": "Success",
  "code": null,
  "data": {
    "exampleField": "exampleValue"
  },
  "details": null
}
```

### Common response fields

- `status`: HTTP status code
- `success`: boolean success flag
- `message`: human-readable message
- `code`: error code for failures (nullable)
- `data`: payload for successful requests
- `details`: validation errors list when applicable

### Validation error shape

```json
{
  "status": 400,
  "success": false,
  "message": "Validasi input gagal.",
  "code": "VALIDATION_ERROR",
  "details": [
    {
      "field": "username",
      "message": "Username wajib diisi."
    }
  ]
}
```

### Common error response

```json
{
  "status": 401,
  "success": false,
  "message": "Username atau password salah.",
  "code": "INVALID_CREDENTIALS"
}
```

## 3. Authentication

### JWT

Most endpoints require a bearer JWT token.

Header:

```http
Authorization: Bearer <token>
```

### Login

Use the login endpoint to obtain the token.

#### POST /api/auth/login

Request body:

```json
{
  "username": "dispatcher.priok",
  "password": "password123"
}
```

Required fields:

- `username`: required, non-empty string
- `password`: required, non-empty string

Successful response example:

```json
{
  "status": 200,
  "success": true,
  "message": "Login berhasil.",
  "data": {
    "token": "<jwt-token>",
    "expiresAt": "2026-08-18T12:00:00Z",
    "user": {
      "id": "<guid>",
      "username": "dispatcher.priok",
      "fullName": "Siti Dispatcher",
      "role": "Dispatcher",
      "assignedHubId": "<hub-guid>"
    }
  }
}
```

### Get current authenticated user

#### GET /api/auth/me

Requires authentication.

Returns the profile of the logged-in user based on the JWT claims.

## 4. Role & Access Matrix

The system uses role-based authorization.

### Available user roles

- `SystemAdmin`
- `Dispatcher`
- `WarehouseStaff`
- `Driver`
- `StoreManager`

### Default seeded accounts

The app seeds these demo users in local development:

| Username | Password | Role |
| --- | --- | --- |
| admin | password123 | SystemAdmin |
| dispatcher.priok | password123 | Dispatcher |
| warehouse.staff | password123 | WarehouseStaff |
| anton.driver | password123 | Driver |
| serang.manager | password123 | StoreManager |

### Endpoint access by role

| Endpoint / Feature | Allowed Roles |
| --- | --- |
| Login | Anonymous |
| Auth Me | Any authenticated user |
| Get hubs | Anonymous |
| Public package tracking | Anonymous |
| Create package | WarehouseStaff, SystemAdmin |
| Load package into manifest | WarehouseStaff, SystemAdmin |
| Create manifest | Dispatcher, SystemAdmin |
| Live fleet | Dispatcher, SystemAdmin |
| Complete manifest | Dispatcher, StoreManager, SystemAdmin |
| Scan milestone | WarehouseStaff, Dispatcher, StoreManager, SystemAdmin |
| Submit POD | Driver, StoreManager, SystemAdmin |
| Damage report | WarehouseStaff, Driver, StoreManager, SystemAdmin |
| Ping telemetry | Driver, SystemAdmin |
| Manage users | SystemAdmin |
| Manage vehicles | SystemAdmin, Dispatcher |
| Fleet SignalR hub | Dispatcher, SystemAdmin |

## 5. Master Data Endpoints

### GET /api/hubs

Public endpoint. Returns all hubs.

Example response:

```json
{
  "status": 200,
  "success": true,
  "message": "Success",
  "data": [
    {
      "id": "<guid>",
      "code": "DC-JKT-PRIOK",
      "name": "Distribution Center Tanjung Priok",
      "type": "DistributionCenter",
      "address": "Jl. Pelabuhan No. 1, Jakarta Utara",
      "latitude": -6.107,
      "longitude": 106.884
    }
  ]
}
```

## 6. Package APIs

### Create package

#### POST /api/packages

Requires: `WarehouseStaff` or `SystemAdmin`

Request body:

```json
{
  "originHubId": "<hub-guid>",
  "destinationHubId": "<hub-guid>",
  "weightKg": 15.5,
  "itemDescription": "Laptop & accessories",
  "isFragile": true
}
```

Required fields:

- `originHubId`: UUID, required
- `destinationHubId`: UUID, required
- `weightKg`: number greater than 0
- `itemDescription`: required string, max 250 chars
- `isFragile`: boolean

Successful response:

```json
{
  "status": 201,
  "success": true,
  "message": "Paket berhasil didaftarkan.",
  "data": {
    "id": "<guid>",
    "trackingNumber": "AWB-20260818-ABCD1234",
    "status": "Created",
    "createdAt": "2026-08-18T10:00:00Z"
  }
}
```

Notes:

- Tracking number format: `AWB-YYYYMMDD-XXXXXXXX`
- Initial status is `Created`
- The package is assigned to the origin hub as `CurrentHubId`

---

### Get package tracking history

#### GET /api/packages/{trackingNumber}/tracking

Public endpoint.

Example:

```http
GET /api/packages/AWB-20260818-ABCD1234/tracking
```

Response includes:

- tracking number
- status
- item description
- weight
- fragile marker
- origin / destination / current hub info
- proof of delivery if delivered
- damage report if applicable
- full checkpoint history

Example response summary:

```json
{
  "status": 200,
  "success": true,
  "data": {
    "trackingNumber": "AWB-20260818-ABCD1234",
    "status": "InTransit",
    "itemDescription": "Laptop & accessories",
    "weightKg": 15.5,
    "isFragile": true,
    "origin": {
      "code": "DC-JKT-PRIOK",
      "name": "Distribution Center Tanjung Priok"
    },
    "destination": {
      "code": "STR-SRG-01",
      "name": "Gerai Retail Serang Kota"
    },
    "currentLocation": {
      "code": "HUB-TNG-01",
      "name": "Transit Hub Tangerang"
    },
    "proofOfDelivery": null,
    "damageIncident": null,
    "history": [
      {
        "status": "Created",
        "location": "Origin Distribution Center",
        "notes": "Paket berhasil didaftarkan dan menunggu proses packing.",
        "updatedBy": "System Registration",
        "timestamp": "2026-08-18T10:00:00Z"
      }
    ]
  }
}
```

---

### Scan milestone / status update

#### POST /api/packages/scan

Requires: `WarehouseStaff`, `Dispatcher`, `StoreManager`, or `SystemAdmin`

Request body:

```json
{
  "trackingNumber": "AWB-20260818-ABCD1234",
  "currentLocationHubId": "<hub-guid>",
  "nextStatus": 5,
  "notes": "Paket tiba di hub dan siap diproses",
  "actorName": "Joko Warehouse"
}
```

Required fields:

- `trackingNumber`: required string
- `currentLocationHubId`: valid hub UUID
- `nextStatus`: enum value for `PackageStatus`
- `notes`: string (can be empty in practice but usually provided)
- `actorName`: required string

#### Package status enum

```text
Created = 1
PackedAndReady = 2
AssignedToManifest = 3
InTransit = 4
ReceivedAtHub = 5
OutForDelivery = 6
Delivered = 7
DeliveredWithIssue = 8
Lost = 9
Damaged = 10
```

Example success response:

```json
{
  "status": 200,
  "success": true,
  "message": "Status milestone berhasil diupdate.",
  "data": {
    "trackingNumber": "AWB-20260818-ABCD1234",
    "currentStatus": "ReceivedAtHub",
    "location": "Distribution Center Tanjung Priok",
    "timestamp": "2026-08-18T10:35:00Z"
  }
}
```

Notes:

- The endpoint performs a simple state transition validation.
- It prevents backward or illegal transitions unless the special cases allow `Damaged` and `Lost` to be set in specific conditions.

---

### Report damaged package

#### POST /api/packages/{trackingNumber}/damage-report

Requires: `WarehouseStaff`, `Driver`, `StoreManager`, or `SystemAdmin`

This endpoint accepts a multipart form upload, not JSON.

Required form fields:

- `reporterName`: string
- `locationHubId`: GUID
- `damageDescription`: string
- `actionTaken`: enum value (`ReturnToOrigin`, `DestroyedOnSite`, `ReplacementDispatched`)
- `photo`: file upload (`IFormFile`)

#### Damage action enum

```text
ReturnToOrigin = 1
DestroyedOnSite = 2
ReplacementDispatched = 3
```

Example multipart form:

```bash
curl -X POST "http://localhost:5172/api/packages/AWB-20260818-ABCD1234/damage-report" \
  -H "Authorization: Bearer $TOKEN" \
  -F "reporterName=Joko Warehouse" \
  -F "locationHubId=<hub-guid>" \
  -F "damageDescription=Karton sobek dan isi barang rusak" \
  -F "actionTaken=ReplacementDispatched" \
  -F "photo=@/path/to/damage.jpg"
```

Success response includes:

- original tracking number
- updated package status
- current location
- damage action taken
- uploaded photo URL
- replacement tracking number if a replacement package is created

---

### Submit proof of delivery (POD)

#### POST /api/packages/{trackingNumber}/pod

Requires: `Driver`, `StoreManager`, or `SystemAdmin`

This is a multipart form endpoint.

Required form fields:

- `recipientName`: required string
- `latitude`: required double
- `longitude`: required double
- `photo`: required file upload

Example:

```bash
curl -X POST "http://localhost:5172/api/packages/AWB-20260818-ABCD1234/pod" \
  -H "Authorization: Bearer $TOKEN" \
  -F "recipientName=Ibu Nia" \
  -F "latitude=-6.120" \
  -F "longitude=106.150" \
  -F "photo=@/path/to/pod.jpg"
```

Validation rules:

- `recipientName` cannot be empty
- `photo` required
- latitude must be valid geolocation within range
- longitude must be valid geolocation within range
- delivery location must be within 1000 meters of the destination hub geofence

On success, the package status becomes `Delivered` and a POD record is stored.

## 7. Manifest APIs

### Create manifest

#### POST /api/manifests

Requires: `Dispatcher` or `SystemAdmin`

Request body:

```json
{
  "originHubId": "<hub-guid>",
  "destinationHubId": "<hub-guid>",
  "driverName": "Pak Anton",
  "vehiclePlate": "B-9999-XYZ"
}
```

Required fields:

- `originHubId`: valid UUID
- `destinationHubId`: valid UUID
- `driverName`: required string
- `vehiclePlate`: required string, normalized and validated against standard Indonesian plate rules

Validation behavior:

- The system validates and normalizes the plate format.
- Vehicle must exist in the database and be active.
- Origin and destination hubs must exist.

On success:

```json
{
  "status": 201,
  "success": true,
  "message": "Surat jalan (manifest) berhasil dibuat.",
  "data": {
    "id": "<guid>",
    "manifestNumber": "MNF-20260818-AB12CD",
    "vehicleModel": "Tronton Box",
    "vehiclePlate": "B-9999-XYZ"
  }
}
```

Manifest number format:

```text
MNF-YYYYMMDD-XXXXXX
```

---

### Load package into manifest

#### POST /api/manifests/{manifestNumber}/load

Requires: `WarehouseStaff` or `SystemAdmin`

Request body:

```json
{
  "trackingNumber": "AWB-20260818-ABCD1234",
  "actorName": "Joko Warehouse"
}
```

Rules:

- Manifest must exist
- Manifest status must still be `Draft`
- Package must exist
- Package must be located at the manifest origin hub
- The package is added to the manifest if not already loaded

On success:

```json
{
  "status": 200,
  "success": true,
  "message": "Paket AWB-20260818-ABCD1234 berhasil diload ke Manifest MNF-20260818-AB12CD.",
  "data": {
    "message": "Paket AWB-20260818-ABCD1234 berhasil diload ke Manifest MNF-20260818-AB12CD.",
    "packageId": "<guid>",
    "manifestId": "<guid>"
  }
}
```

---

### Complete manifest

#### POST /api/manifests/{manifestNumber}/complete

Requires: `Dispatcher`, `StoreManager`, or `SystemAdmin`

Request body:

```json
{
  "unloadLocationHubId": "<hub-guid>",
  "scannedTrackingNumbers": [
    "AWB-20260818-ABCD1234",
    "AWB-20260818-XYZ98765"
  ],
  "actorName": "Ibu Siti Store Manager"
}
```

Required fields:

- `unloadLocationHubId`: valid destination hub UUID
- `scannedTrackingNumbers`: array of tracking numbers physically scanned on arrival
- `actorName`: required string

Behavior:

- It compares system-loaded packages with physically scanned packages.
- Any package not present in the scanned list is marked as `Lost` and counted as missing.
- Matching packages are marked `ReceivedAtHub` and assigned to the unload location.
- Manifest is marked as:
  - `CompletedClean` if no discrepancy
  - `CompletedWithDiscrepancy` if there are missing packages

Example success response:

```json
{
  "status": 200,
  "success": true,
  "message": "Manifest berhasil ditutup.",
  "data": {
    "manifestStatus": "CompletedClean",
    "totalExpected": 10,
    "totalReceived": 10,
    "totalMissing": 0
  }
}
```

## 8. Fleet / Telemetry APIs

### Ping telemetry

#### POST /api/telemetry/ping

Requires: `Driver` or `SystemAdmin`

Request body:

```json
{
  "vehiclePlate": "B-9999-XYZ",
  "latitude": -6.107,
  "longitude": 106.884,
  "speedKmh": 58.5,
  "headingDegrees": 120
}
```

Required fields:

- `vehiclePlate`: required
- `latitude`: numeric between -90 and 90
- `longitude`: numeric between -180 and 180
- `speedKmh`: number greater than or equal to 0
- `headingDegrees`: number between 0 and 360

Behavior:

- Writes a hot Redis snapshot for live tracking.
- Queues telemetry to cold storage in PostgreSQL.
- Broadcasts fleet update through SignalR.
- Flags unauthorized movement when a vehicle moves above 5 km/h without an active manifest assignment.

Example response:

```json
{
  "status": 200,
  "success": true,
  "message": "Telemetry ping recorded in Hot & Cold storage.",
  "data": {
    "vehiclePlate": "B-9999-XYZ",
    "isOnDuty": true,
    "activeManifest": "MNF-20260818-AB12CD",
    "hasAlert": false,
    "timestamp": "2026-08-18T10:45:00Z"
  }
}
```

---

### Live fleet status

#### GET /api/fleet/{vehiclePlate}/live

Requires: `Dispatcher` or `SystemAdmin`

Example:

```http
GET /api/fleet/B-9999-XYZ/live
```

Returns:

- connectivity status (`ONLINE` / `OFFLINE_SIGNAL_LOST`)
- last ping time
- last known coordinates
- speed and heading
- operational status
- active manifest number
- security alert information

Example response snippet:

```json
{
  "status": 200,
  "success": true,
  "data": {
    "vehiclePlate": "B-9999-XYZ",
    "connectivity": {
      "status": "ONLINE",
      "lastPingUtc": "2026-08-18T10:45:00Z",
      "minutesOffline": 0
    },
    "lastKnownCoordinates": {
      "latitude": -6.107,
      "longitude": 106.884,
      "speedKmh": 58.5,
      "headingDegrees": 120
    },
    "operationalStatus": {
      "isAssigned": true,
      "status": "ON_DUTY",
      "activeManifestNumber": "MNF-20260818-AB12CD",
      "driverName": "Pak Anton",
      "totalPackagesCarried": 15
    },
    "securityAlert": {
      "hasAlert": false,
      "alertType": null,
      "description": null
    }
  }
}
```

---

### Fleet history playback

#### GET /api/fleet/{vehiclePlate}/history

Requires: `Dispatcher` or `SystemAdmin`

Optional query parameters:

- `fromUtc`: ISO datetime filter start
- `toUtc`: ISO datetime filter end

Example:

```http
GET /api/fleet/B-9999-XYZ/history?fromUtc=2026-08-18T00:00:00Z&toUtc=2026-08-18T23:59:59Z
```

Returns all recorded telemetry points in the window.

## 9. User Management APIs

### List users

#### GET /api/users

Requires: `SystemAdmin`

Returns all users sorted by role and name.

### Create user

#### POST /api/users

Requires: `SystemAdmin`

Request body:

```json
{
  "username": "new.dispatcher",
  "password": "password123",
  "fullName": "Budi Dispatcher Baru",
  "role": 2,
  "assignedHubId": "<hub-guid>"
}
```

Required fields:

- `username`: min 3 chars, max 50
- `password`: min 6 chars
- `fullName`: required, max 100
- `role`: valid enum value (`SystemAdmin`, `Dispatcher`, `WarehouseStaff`, `Driver`, `StoreManager`)
- `assignedHubId`: optional hub UUID

### Update user

#### PUT /api/users/{id}

Requires: `SystemAdmin`

Request body:

```json
{
  "fullName": "Budi Dispatcher Baru",
  "role": 2,
  "assignedHubId": "<hub-guid>",
  "isActive": true
}
```

### Delete / deactivate user

#### DELETE /api/users/{id}

Requires: `SystemAdmin`

Performs a soft delete by setting `IsActive = false`.

## 10. Vehicle Management APIs

### List vehicles

#### GET /api/vehicles

Requires: `SystemAdmin` or `Dispatcher`

### Create vehicle

#### POST /api/vehicles

Requires: `SystemAdmin` or `Dispatcher`

Request body:

```json
{
  "plateNumber": "B-9999-XYZ",
  "modelType": "Tronton Box",
  "maxWeightCapacityKg": 15000
}
```

Validation details:

- plate is normalized and validated
- duplicates are rejected
- max weight must be greater than 0

### Update vehicle

#### PUT /api/vehicles/{id}

Requires: `SystemAdmin` or `Dispatcher`

### Disable vehicle

#### DELETE /api/vehicles/{id}

Requires: `SystemAdmin` or `Dispatcher`

Soft-disables the vehicle by setting `IsActive = false`.

## 11. SignalR Hub

Endpoint:

```text
/hubs/fleet
```

Authorized roles:

- `Dispatcher`
- `SystemAdmin`

This hub streams live fleet updates to connected clients.

The client may use the JWT token in the query string as:

```text
?access_token=<jwt-token>
```

## 12. Example Full Flow

A typical workflow:

1. Login as dispatcher or warehouse staff.
2. Get hubs list.
3. Create vehicle (if needed).
4. Create package.
5. Create manifest.
6. Load package into manifest.
7. Driver sends telemetry ping.
8. Dispatcher monitors live fleet via `/api/fleet/{vehiclePlate}/live` or SignalR.
9. Complete manifest on arrival.
10. Submit POD when delivered.
11. Query tracking history publicly via `/api/packages/{trackingNumber}/tracking`.

## 13. Notes

- The API is designed for local development and demo/test usage.
- The default seeded accounts use `password123`.
- JWT tokens expire after 8 hours.
- Some routes upload files using multipart form data and store them in MinIO/S3-compatible storage.
- The API expects standard JSON payloads for most endpoints and form-data for POD/damage endpoints.

## 14. Quick cURL Examples

### Login

```bash
curl -sS -X POST "http://localhost:5172/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "dispatcher.priok",
    "password": "password123"
  }'
```

### Create manifest

```bash
curl -sS -X POST "http://localhost:5172/api/manifests" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "originHubId": "<origin-hub-guid>",
    "destinationHubId": "<destination-hub-guid>",
    "driverName": "Pak Anton",
    "vehiclePlate": "B-9999-XYZ"
  }'
```

### Get tracking history

```bash
curl -sS "http://localhost:5172/api/packages/AWB-20260818-ABCD1234/tracking"
```

### Live fleet

```bash
curl -sS -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5172/api/fleet/B-9999-XYZ/live"
```

---

This documentation reflects the actual routes and behavior implemented in the current application codebase.
