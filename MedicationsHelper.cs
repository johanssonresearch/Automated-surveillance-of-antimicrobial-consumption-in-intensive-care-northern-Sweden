using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VLL.MetaVision.IntegrationManager;
using VLL.MetaVision.MedicationManager;
using VLL.MetaVision.Model;
using VLL.MetaVision.ProductionDataRepository;
using Encounter = VLL.MetaVision.ProductionDataRepository.Encounter;
using MedicationAdministration = VLL.MetaVision.ProductionDataRepository.MedicationAdministration;

namespace VLL.MetaVision.ProductionDataService
{
    internal class MedicationsHelper
    {
        private readonly IMetaVisionHelper metaVisionHelper;

        private static readonly NLog.Logger logger = NLog.LogManager.GetLogger("UserLog");

        private static string[] _antibiotics;
        private static string[] _fungiGroups;

        private readonly int _vDrainParameterId;

        public MedicationsHelper(IMetaVisionHelper metaVisionHelper, IProductionDataRepository repository)
        {
            this.metaVisionHelper = metaVisionHelper;

            _antibiotics = repository.GetAntibioticsAtcCodes();
            _fungiGroups = repository.GetAntimycoticsAtcCodes();

            _vDrainParameterId = int.Parse(System.Configuration.ConfigurationManager.AppSettings["VDrainParameterId"]);
        }

        internal void FillMedications(Encounter encounter)
        {
            FillMedicationOrders(encounter);
            FillMedicationAdministrations(encounter);
        }

        internal void FillMedicationAdministrations(Encounter encounter)
        {
            int intpid = int.Parse(encounter.SourceEncounterId);
            var metaVisionManager = metaVisionHelper.GetMetaVisionManager();

            var medicationAdministrations = metaVisionManager.GetMedicationAdministrations(encounter.PatientId,
                encounter.PeriodStart, encounter.PeriodEnd, intpid);

            var medicationAdministrationList = new List<MedicationAdministration>();
            foreach (var medicationAdministration in medicationAdministrations)
            {
                if (!medicationAdministration.WasNotGiven &&
                    (medicationAdministration.EffectiveTimeStart >= encounter.PeriodStart) &&
                    (medicationAdministration.EffectiveTimeEnd <= encounter.PeriodEnd))
                {
                    foreach (var medicationAdministrationMedication in medicationAdministration.Medications)
                    {
                        if ((medicationAdministrationMedication.Type == MedicationType.MainComponent) &&
                            !string.IsNullOrEmpty(medicationAdministrationMedication.ATCCode))
                        {
                            if (_antibiotics.Contains(medicationAdministrationMedication.ATCCode) ||
                                _fungiGroups.Any(x => medicationAdministrationMedication.ATCCode.StartsWith(x)))
                            {
                                var ma = new MedicationAdministration()
                                {
                                    Name = medicationAdministrationMedication.Name,
                                    AtcCode = medicationAdministrationMedication.ATCCode,
                                    Quantity = (decimal?) medicationAdministrationMedication.Quantity,
                                    Unit = medicationAdministrationMedication.Unit,
                                    StartTime = medicationAdministration.EffectiveTimeStart,
                                    EndTime = medicationAdministration.EffectiveTimeEnd,
                                    Route = medicationAdministration.Dosage.Route,
                                    Site = medicationAdministration.Dosage.Site,
                                    IsAntibiotics = true,
                                    DoseCount = 1
                                };

                                medicationAdministrationList.Add(ma);
                            }
                        }
                    }
                }
            }

            var compactList = new List<MedicationAdministration>();
            foreach (var medicationAdministration in medicationAdministrationList)
            {
                var previous = compactList.OrderByDescending(x => x.StartTime).FirstOrDefault(x =>
                    (x.Name == medicationAdministration.Name) && (x.AtcCode == medicationAdministration.AtcCode) &&
                    (x.Quantity == medicationAdministration.Quantity) && (x.Unit == medicationAdministration.Unit));

                if ((previous != null) && ((previous.EndTime.Date == medicationAdministration.EndTime.Date) ||
                                           (previous.EndTime.Date.AddDays(1) == medicationAdministration.EndTime.Date)))
                {
                    previous.EndTime = medicationAdministration.EndTime;
                    previous.DoseCount++;
                }
                else
                {
                    compactList.Add(new MedicationAdministration()
                    {
                        Name = medicationAdministration.Name,
                        AtcCode = medicationAdministration.AtcCode,
                        Quantity = medicationAdministration.Quantity,
                        Unit = medicationAdministration.Unit,
                        StartTime = medicationAdministration.StartTime,
                        EndTime = medicationAdministration.EndTime,
                        Route = medicationAdministration.Route,
                        Site = medicationAdministration.Site,
                        IsAntibiotics = true,
                        DoseCount = 1
                    });
                }
            }

            encounter.MedicationAdministrations = compactList;
        }

        private void FillMedicationOrders(Encounter encounter)
        {
            try
            {
                int intpid = int.Parse(encounter.SourceEncounterId);
                var metaVisionManager = metaVisionHelper.GetMetaVisionManager();

                // Must get full ParameterText object, to be able to sort on times against orders
                var comReqList = metaVisionManager
                    .GetParameterTextValues(23576, intpid, metaVisionHelper.StartTime, metaVisionHelper.EndTime)
                    .ToList();
                var careReqList = metaVisionManager
                    .GetParameterTextValues(23578, intpid, metaVisionHelper.StartTime, metaVisionHelper.EndTime)
                    .ToList();
                var profylaxList = metaVisionManager
                    .GetParameterTextValues(23577, intpid, metaVisionHelper.StartTime, metaVisionHelper.EndTime)
                    .ToList();
                var otherList = metaVisionManager
                    .GetParameterTextValues(23663, intpid, metaVisionHelper.StartTime, metaVisionHelper.EndTime)
                    .ToList();
                var infGradeList = metaVisionManager
                    .GetParameterTextValues(23579, intpid, metaVisionHelper.StartTime, metaVisionHelper.EndTime)
                    .ToList();
                var infCatGradeList = metaVisionManager
                    .GetParameterTextValues(23580, intpid, metaVisionHelper.StartTime, metaVisionHelper.EndTime)
                    .ToList();
                var vDrainList = metaVisionManager
                    .GetParameterTextValues(_vDrainParameterId, intpid, metaVisionHelper.StartTime, metaVisionHelper.EndTime)
                    .ToList();

                var medications = metaVisionManager.GetMedicationOrders(intpid, metaVisionHelper.StartTime, metaVisionHelper.EndTime);

                var medicationOrderList = new List<ProductionDataRepository.MedicationOrder>();

                foreach (var medicationOrder in medications)
                {
                    if (!string.IsNullOrEmpty(medicationOrder.AtcCode))
                    {
                        // Right now, we are only collection antibiotics (and the motivation fields)
                        if (_antibiotics.Contains(medicationOrder.AtcCode) ||
                            _fungiGroups.Any(x => medicationOrder.AtcCode.StartsWith(x)))
                        {
                            var mo = new ProductionDataRepository.MedicationOrder()
                            {
                                OrderId = medicationOrder.OrderID,
                                MedicationId = medicationOrder.MedicationId,
                                MedicationName = medicationOrder.MedicationName,
                                AtcCode = medicationOrder.AtcCode,
                                IsAntibiotics = true,
                                Amount = medicationOrder.Amount,
                                Unit = medicationOrder.Unit,
                                OrderedBy = medicationOrder.OrderByName,
                                OrderedById = medicationOrder.OrderByID,
                                OrderedAt = medicationOrder.OrderAt,
                                Location = medicationOrder.Location,
                                Route = medicationOrder.Route
                            };

                            // Get the order save time
                            var orderTime = metaVisionManager.GetMedicationOrderTime(intpid, medicationOrder.OrderID);

                            // Get the reason for the order (from one of 4 parameters)
                            // Sort them, so to take the latest next to the order form save time.
                            var reasonsList = new List<ParameterTextData>();

                            var comReq = comReqList.OrderByDescending(x => x.Time)
                                .FirstOrDefault(x => x.Time == orderTime);
                            if (comReq != null)
                                reasonsList.Add(comReq);

                            var careReq = careReqList.OrderByDescending(x => x.Time)
                                .FirstOrDefault(x => x.Time == orderTime);
                            if (careReq != null)
                                reasonsList.Add(careReq);

                            var profReq = profylaxList.OrderByDescending(x => x.Time)
                                .FirstOrDefault(x => x.Time == orderTime);
                            if (profReq != null)
                                reasonsList.Add(profReq);

                            var otherReq = otherList.OrderByDescending(x => x.Time)
                                .FirstOrDefault(x => x.Time == orderTime);
                            if (otherReq != null)
                                reasonsList.Add(otherReq);

                            // Pick the last text saved before this order was saved.
                            var reason = reasonsList.OrderByDescending(x => x.Time).FirstOrDefault();
                            if (reason != null)
                            {
                                mo.OrderReason = reason.Value;
                                mo.OrderReasonType = reason.Name;

                                var grade = infGradeList.FirstOrDefault(x => x.Time == reason.Time) ??
                                            infCatGradeList.FirstOrDefault(x => x.Time == reason.Time);

                                if (grade != null)
                                {
                                    mo.OrderReasonGrade = grade.Value;
                                }

                                var vDrain = vDrainList.FirstOrDefault(x => x.Time == reason.Time);
                                if (!string.IsNullOrEmpty(vDrain?.Value))
                                {
                                    mo.OrderReason = string.Concat(mo.OrderReason, " ", vDrain.Value);
                                }
                            }

                            medicationOrderList.Add(mo);
                        }
                    }
                }

                encounter.MedicationOrders = medicationOrderList;
            }
            catch (Exception e)
            {
                logger.Error(e);
            }
        }
    }
}
