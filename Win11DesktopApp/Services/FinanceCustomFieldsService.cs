using System;
using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public class FinanceCustomFieldsService
    {
        private readonly IFinanceCustomFieldsStorage? _customFieldsStorage;

        public FinanceCustomFieldsService(IFinanceCustomFieldsStorage? customFieldsStorage)
        {
            _customFieldsStorage = customFieldsStorage;
        }

        public List<CustomSalaryField> GetCustomFields()
        {
            return RequireLocalDb().GetCustomSalaryFields()
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

            RequireLocalDb().UpsertCustomSalaryField(field);
        }

        public void UpdateCustomField(CustomSalaryField updated)
        {
            RequireLocalDb().UpsertCustomSalaryField(updated);
        }

        public void ReorderCustomFields(List<CustomSalaryField> orderedFields)
        {
            for (int i = 0; i < orderedFields.Count; i++)
                orderedFields[i].Order = i;

            RequireLocalDb().ReplaceCustomSalaryFields(orderedFields);
        }

        public bool RemoveCustomField(string fieldId)
        {
            if (string.IsNullOrWhiteSpace(fieldId))
                return false;

            RequireLocalDb().DeleteCustomSalaryField(fieldId);
            return true;
        }

        private List<CustomSalaryField> GetFieldsSnapshot()
        {
            return RequireLocalDb().GetCustomSalaryFields();
        }

        private IFinanceCustomFieldsStorage RequireLocalDb()
        {
            if (_customFieldsStorage == null)
                throw new InvalidOperationException("Custom fields storage is required.");

            return _customFieldsStorage;
        }
    }
}
