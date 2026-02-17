# Data Persistence System

## Overview
The Agency Contractor application uses a secure, file-based persistence system to store company data. The system is designed to ensure data integrity, security, and recoverability.

## File Structure
All data is stored in the root folder selected by the user (configured in Settings).

```
{RootFolder}/
├── company_data.json           # Main data file (Encrypted)
├── company_data.json.sha256    # Integrity checksum
└── backups/                    # Backup folder
    ├── company_data_20240101_120000.json.bak
    └── ...
```

## Data Format
The `company_data.json` file contains an encrypted JSON array of `EmployerCompany` objects.
Decrypted JSON structure:
```json
[
  {
    "Id": "guid-uuid-...",
    "CreatedAt": "2024-01-01T12:00:00",
    "LastModified": "2024-01-02T15:30:00",
    "Name": "Company Name",
    "ICO": "12345678",
    "Addresses": [...],
    "Positions": [...],
    "Tags": {...}
  },
  ...
]
```

## Security Features

### Encryption
- **Algorithm**: AES-256 (CBC mode).
- **Key**: Application-specific key derived securely (hardcoded for demo, configurable in production).
- **Scope**: The entire `company_data.json` file content is encrypted.

### Integrity Check
- **Algorithm**: SHA-256.
- **Mechanism**: A checksum file (`.sha256`) is generated after every save.
- **Validation**: On load, the application computes the hash of the encrypted file and compares it with the stored checksum. If they mismatch, the load is aborted to prevent data corruption or tampering.

### Backup System
- **Trigger**: Automatically triggered before overwriting `company_data.json`.
- **Retention**: Keeps the last 10 backups.
- **Format**: Copies of the encrypted file with timestamped filenames.

## Recovery Procedures
In case of data corruption or accidental loss:
1.  Navigate to the `{RootFolder}/backups/` directory.
2.  Locate the most recent valid backup file (e.g., `company_data_YYYYMMDD_HHMMSS.json.bak`).
3.  Rename it to `company_data.json` and move it to the `{RootFolder}/`.
4.  Restart the application.

## Testing
The persistence layer is covered by Unit Tests in `Win11DesktopApp.Tests`.
Key test cases:
- Successful Save/Load.
- Backup creation on update.
- Integrity check failure on corrupted data.
