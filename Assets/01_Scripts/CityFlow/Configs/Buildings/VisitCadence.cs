using System;
using UnityEngine;

namespace CityFlow.Content
{
    [Serializable]
    public struct VisitCadence
    {
        [SerializeField, Min(0)]
        private int visitsPerPeriod;

        [SerializeField, Min(1)]
        private int periodDays;

        public VisitCadence(int visitsPerPeriod, int periodDays)
        {
            this.visitsPerPeriod = Mathf.Max(0, visitsPerPeriod);
            this.periodDays = Mathf.Max(1, periodDays);
        }

        public int VisitsPerPeriod => Mathf.Max(0, visitsPerPeriod);
        public int PeriodDays => Mathf.Max(1, periodDays);
        public float VisitsPerDay =>
            VisitsPerPeriod / (float)PeriodDays;

        public VisitCadence Sanitized() =>
            new VisitCadence(VisitsPerPeriod, PeriodDays);
    }
}
