using System;
using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public class FinanceCustomFieldsService
    {
        private readonly LocalDbService? _localDbService;
        private readonly IList<CustomSalaryField> _customFields;
        private bool _useLocalDb;

        public FinanceCustomFieldsService(LocalDbService? localDbService, IList<CustomSalaryField> customFields)
        {
            _localDbService = localDbService;
            _customFields = customFields;
        }

        public bool UseLocalDb => _useLocalDb;

        public LocalDbMigrationResult EnsureMigratedToLocalDb()
        {
            try
            {
                if (_localDbService == null)
                    return new LocalDbMigrationResult { Message = "LocalDbService is not configured." };

                var result = _localDbService.MigrateCustomFieldsIfNeeded(_customFields.ToList());
                _useLocalDb = result.IsSuccessful;

                if (!result.WasMigrationAttempted && _localDbService.IsCustomFieldsMigrationCompleted())
                    _useLocalDb = true;

                return result;
            }
            catch (Exception ex)
            {
                _useLocalDb = false;
                LoggingService.LogError("FinanceCustomFieldsService.EnsureMigratedToLocalDb", ex);
                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = false,
                    Message = ex.Message
                };
            }
        }

        public List<CustomSalaryField> GetCustomFields()
        {
            if (_useLocalDb && _localDbService != null)
            {
                var dbFields = _localDbService.GetCustomSalaryFields();
                if (dbFields.Count > 0 || _localDbService.IsCustomFieldsMigrationCompleted())
                    return dbFields;
            }

            return _customFields
                .OrderBy(f => f.FirmName)
                .ThenBy(f => f.Order)
                .ToList();
        }

        public List<CustomSalaryField> GetFieldsForFirm(string firmName)
        {
            return GetFieldsSnapshot()
                .Where(f => f.FirmName == FinanceConstants.AllFirmsKey || f.FirmName == firmName)
                .OrderBy(f => f.Order)
                .ToList();
        }

        public List<CustomSalaryField> GetActiveFields(IEnumerable<string> visibleFirms)
        {
            var firmSet = visibleFirms.ToHashSet();
            return GetFieldsSnapshot()
                .Where(f => f.FirmName == FinanceConstants.AllFirmsKey || firmSet.Contains(f.FirmName))
                .OrderBy(f => f.Order)
                .ToList();
        }

        public void AddCustomField(CustomSalaryField field)
        {
            if (string.IsNullOrEmpty(field.Id))
                field.Id = Guid.NewGuid().ToString();

            if (_useLocalDb && _localDbService != null)
            {
                _localDbService.UpsertCustomSalaryField(field);
                return;
            }

            _customFields.Add(field);
        }

        public void UpdateCustomField(CustomSalaryField updated)
        {
            if (_useLocalDb && _localDbService != null)
            {
                _localDbService.UpsertCustomSalaryField(updated);
                return;
            }

            for (int i = 0; i < _customFields.Count; i++)
            {
                if (_customFields[i].Id != updated.Id)
                    continue;

                _customFields[i] = updated;
                break;
            }
        }

        public void ReorderCustomFields(List<CustomSalaryField> orderedFields)
        {
            if (_useLocalDb && _localDbService != null)
            {
                for (int i = 0; i < orderedFields.Count; i++)
                    orderedFields[i].Order = i;

                _localDbService.ReplaceCustomSalaryFields(orderedFields);
                return;
            }

            for (int i = 0; i < orderedFields.Count; i++)
            {
                var db = _customFields.FirstOrDefault(f => f.Id == orderedFields[i].Id);
                if (db != null)
                    db.Order = i;
            }
        }

        public bool RemoveCustomField(string fieldId)
        {
            if (string.IsNullOrWhiteSpace(fieldId))
                return false;

            if (_useLocalDb && _localDbService != null)
            {
                _localDbService.DeleteCustomSalaryField(fieldId);
                return true;
            }

            var removed = false;
            for (int i = _customFields.Count - 1; i >= 0; i--)
            {
                if (_customFields[i].Id != fieldId)
                    continue;

                _customFields.RemoveAt(i);
                removed = true;
            }

            return removed;
        }

        private List<CustomSalaryField> GetFieldsSnapshot()
        {
            if (_useLocalDb && _localDbService != null)
            {
                var dbFields = _localDbService.GetCustomSalaryFields();
                if (dbFields.Count > 0 || _localDbService.IsCustomFieldsMigrationCompleted())
                    return dbFields;
            }

            return _customFields.ToList();
        }
    }
}
