using UnityEngine;

using RTSEngine.Entities;

namespace RTSEngine.Attack
{
    [System.Serializable]
    public struct DamageData
    {
        [Tooltip("Default damage value to deal units that do not have a custom damage enabled.")]
        public int unitMin;
        [Tooltip("Default damage value to deal units that do not have a custom damage enabled.")]
        public int unitMax;
        [Tooltip("Default damage value to deal buildings that do not have a cstuom damage enabled.")]
        public int buildingMin;
        [Tooltip("Default damage value to deal buildings that do not have a cstuom damage enabled.")]
        public int buildingMax;

        [Tooltip("Define custom damage values for unit and building types.")]
        public CustomDamageData[] custom;

        public int Get (IFactionEntity target)
        {
            foreach (CustomDamageData cd in custom)
                if (cd.code.Contains(target))
                    return cd.damage;


            return target.IsUnit()
                ? Random.Range(unitMin, unitMax)
                : Random.Range(buildingMin, buildingMax);
        }
    }
}
