using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// Creates one physical ambulance for each dispatched incident and
    /// keeps the vehicle lifetime synchronized with the incident system.
    /// </summary>
    public sealed class AmbulanceDispatchService :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [SerializeField]
        private EmergencyIncidentSystem incidentSystem;
        [SerializeField]
        private EmergencyIncidentConfigSO config;
        [SerializeField]
        private GameObject ambulanceVehiclePrefab;

        private readonly Dictionary<int, AmbulanceVehicleAgent>
            activeVehicles = new();
        private readonly Dictionary<
            Vector2Int,
            List<AmbulanceVehicleAgent>>
            hospitalVehicles = new();
        private readonly List<Vector2Int>
            hospitalReleaseBuffer = new();

        private CityFlowServices services;
        private bool initialized;
        private bool subscribed;
        private bool hospitalFleetSyncPending;
        private bool activeIncidentSyncPending;

        public int ActiveVehicleCount =>
            activeVehicles.Count;
        public int TotalVehicleCount
        {
            get
            {
                int count = 0;
                foreach (List<AmbulanceVehicleAgent> vehicles
                         in hospitalVehicles.Values)
                {
                    count += vehicles.Count;
                }

                return count;
            }
        }
        public int ParkedVehicleCount =>
            Mathf.Max(
                0,
                TotalVehicleCount -
                ActiveVehicleCount);

        public void Initialize(CityFlowServices cityServices)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            ResolveReferences();

            if (cityServices == null ||
                incidentSystem == null ||
                config == null ||
                ambulanceVehiclePrefab == null ||
                ambulanceVehiclePrefab.GetComponent<
                    AmbulanceVehicleAgent>() == null)
            {
                Debug.LogError(
                    "[AmbulanceDispatchService] Incident system, config, services, and ambulance vehicle prefab are required.",
                    this);
                return;
            }

            services = cityServices;
            initialized = true;
            Subscribe();
            SynchronizeHospitalFleet();
            SynchronizeActiveIncidents();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (initialized)
            {
                Subscribe();
                SynchronizeActiveIncidents();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            ReleaseAllVehicles();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void LateUpdate()
        {
            if (!hospitalFleetSyncPending &&
                !activeIncidentSyncPending)
            {
                return;
            }

            if (hospitalFleetSyncPending)
            {
                hospitalFleetSyncPending = false;
                SynchronizeHospitalFleet();
            }

            if (activeIncidentSyncPending)
            {
                activeIncidentSyncPending = false;
                SynchronizeActiveIncidents();
            }
        }

        private void ResolveReferences()
        {
            incidentSystem ??=
                GetComponent<EmergencyIncidentSystem>();
            config ??= incidentSystem?.Config;
        }

        private void Subscribe()
        {
            if (subscribed || incidentSystem == null)
            {
                return;
            }

            incidentSystem.IncidentCreated +=
                HandleIncidentChanged;
            incidentSystem.IncidentChanged +=
                HandleIncidentChanged;
            incidentSystem.IncidentRemoved +=
                HandleIncidentRemoved;
            if (services?.Events != null)
            {
                services.Events.Placed += HandlePlaced;
            }
            if (services?.Save != null)
            {
                services.Save.RestoreCompleted +=
                    HandleRestoreCompleted;
            }
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || incidentSystem == null)
            {
                return;
            }

            incidentSystem.IncidentCreated -=
                HandleIncidentChanged;
            incidentSystem.IncidentChanged -=
                HandleIncidentChanged;
            incidentSystem.IncidentRemoved -=
                HandleIncidentRemoved;
            if (services?.Events != null)
            {
                services.Events.Placed -= HandlePlaced;
            }
            if (services?.Save != null)
            {
                services.Save.RestoreCompleted -=
                    HandleRestoreCompleted;
            }
            subscribed = false;
        }

        private void HandlePlaced(PlacedEvent placed)
        {
            if (placed.Type == TileType.Hospital)
            {
                // EmergencyIncidentSystem consumes the same placement event.
                // Defer until every subscriber has updated its hospital list,
                // otherwise the ambulance is first created when a call starts.
                hospitalFleetSyncPending = true;
            }
        }

        private void HandleRestoreCompleted(
            RestoreCompletedEvent _)
        {
            // Defer until every restore subscriber has rebuilt its data.
            hospitalFleetSyncPending = true;
            activeIncidentSyncPending = true;
        }

        private void SynchronizeActiveIncidents()
        {
            if (incidentSystem == null)
            {
                return;
            }

            IReadOnlyList<EmergencyIncident> incidents =
                incidentSystem.ActiveIncidents;

            for (int i = 0; i < incidents.Count; i++)
            {
                HandleIncidentChanged(incidents[i]);
            }
        }

        private void HandleIncidentChanged(
            EmergencyIncident incident)
        {
            if (incident == null)
            {
                return;
            }

            if (incident.State is
                EmergencyIncidentState.AmbulanceOutbound
                or EmergencyIncidentState.Treating)
            {
                EnsureVehicle(incident);
                return;
            }

            if (incident.State is
                EmergencyIncidentState.AmbulanceReturning
                or EmergencyIncidentState
                    .AmbulanceReturningAfterFailure)
            {
                EnsureVehicle(incident);

                if (activeVehicles.TryGetValue(
                        incident.IncidentId,
                        out AmbulanceVehicleAgent returningVehicle))
                {
                    returningVehicle.BeginReturn();
                }

                return;
            }

            if (incident.IsFinished)
            {
                ReleaseVehicle(incident.IncidentId);
                SynchronizeHospitalFleet();
            }
        }

        private void HandleIncidentRemoved(
            EmergencyIncident incident)
        {
            if (incident != null)
            {
                ReleaseVehicle(incident.IncidentId);
                SynchronizeHospitalFleet();
            }
        }

        private void EnsureVehicle(
            EmergencyIncident incident)
        {
            if (activeVehicles.ContainsKey(
                    incident.IncidentId))
            {
                return;
            }

            SynchronizeHospitalFleet();
            AmbulanceVehicleAgent agent =
                FindAvailableVehicle(
                    incident.AssignedHospital);
            if (agent == null)
            {
                Debug.LogError(
                    $"[AmbulanceDispatchService] No parked ambulance is available at hospital {incident.AssignedHospital}.",
                    this);
                return;
            }

            activeVehicles.Add(
                incident.IncidentId,
                agent);

            agent.Initialize(services);

            bool assigned =
                incident.State ==
                    EmergencyIncidentState.AmbulanceOutbound
                    ? agent.Assign(
                        incident,
                        incidentSystem)
                    : agent.RestoreAssignment(
                        incident,
                        incidentSystem);

            if (assigned)
            {
                return;
            }

            Debug.LogError(
                $"[AmbulanceDispatchService] Could not assign ambulance to incident #{incident.IncidentId}.",
                this);
            ReleaseVehicle(incident.IncidentId);
        }

        private void ReleaseVehicle(int incidentId)
        {
            if (!activeVehicles.Remove(
                    incidentId,
                    out AmbulanceVehicleAgent agent) ||
                agent == null)
            {
                return;
            }

            agent.Release();
        }

        private void ReleaseAllVehicles()
        {
            foreach (List<AmbulanceVehicleAgent> vehicles
                     in hospitalVehicles.Values)
            {
                for (int i = 0; i < vehicles.Count; i++)
                {
                    DestroyVehicle(vehicles[i]);
                }
            }

            activeVehicles.Clear();
            hospitalVehicles.Clear();
            hospitalReleaseBuffer.Clear();
        }

        private void SynchronizeHospitalFleet()
        {
            if (!initialized ||
                incidentSystem == null ||
                config == null)
            {
                return;
            }

            IReadOnlyList<Vector2Int> hospitals =
                incidentSystem.HospitalTiles;
            hospitalReleaseBuffer.Clear();

            foreach (Vector2Int hospital
                     in hospitalVehicles.Keys)
            {
                if (!ContainsHospital(
                        hospitals,
                        hospital))
                {
                    hospitalReleaseBuffer.Add(hospital);
                }
            }

            for (int i = 0;
                 i < hospitalReleaseBuffer.Count;
                 i++)
            {
                RemoveHospitalFleet(
                    hospitalReleaseBuffer[i]);
            }

            for (int hospitalIndex = 0;
                 hospitalIndex < hospitals.Count;
                 hospitalIndex++)
            {
                Vector2Int hospital =
                    hospitals[hospitalIndex];
                if (!hospitalVehicles.TryGetValue(
                        hospital,
                        out List<AmbulanceVehicleAgent>
                            vehicles))
                {
                    vehicles =
                        new List<AmbulanceVehicleAgent>(
                            config.AmbulancesPerHospital);
                    hospitalVehicles.Add(
                        hospital,
                        vehicles);
                }

                while (vehicles.Count <
                       config.AmbulancesPerHospital)
                {
                    int slot = vehicles.Count;
                    AmbulanceVehicleAgent agent =
                        CreateVehicle(
                            hospital,
                            slot);
                    if (agent == null)
                    {
                        break;
                    }

                    vehicles.Add(agent);
                }
            }

            hospitalReleaseBuffer.Clear();
        }

        private AmbulanceVehicleAgent CreateVehicle(
            Vector2Int hospital,
            int parkingSlot)
        {
            GameObject instance = Instantiate(
                ambulanceVehiclePrefab,
                transform);
            instance.name =
                $"Ambulance_{hospital.x}_{hospital.y}_{parkingSlot}";
            AmbulanceVehicleAgent agent =
                instance.GetComponent<
                    AmbulanceVehicleAgent>();
            agent.Initialize(services);

            if (agent.PrepareAtHospital(
                    hospital,
                    parkingSlot))
            {
                return agent;
            }

            DestroyVehicle(agent);
            return null;
        }

        private AmbulanceVehicleAgent
            FindAvailableVehicle(Vector2Int hospital)
        {
            if (!hospitalVehicles.TryGetValue(
                    hospital,
                    out List<AmbulanceVehicleAgent> vehicles))
            {
                return null;
            }

            for (int i = 0; i < vehicles.Count; i++)
            {
                AmbulanceVehicleAgent agent = vehicles[i];
                if (agent != null && !agent.IsAssigned)
                {
                    return agent;
                }
            }

            return null;
        }

        private void RemoveHospitalFleet(
            Vector2Int hospital)
        {
            if (!hospitalVehicles.Remove(
                    hospital,
                    out List<AmbulanceVehicleAgent> vehicles))
            {
                return;
            }

            for (int i = 0; i < vehicles.Count; i++)
            {
                if (vehicles[i] != null &&
                    vehicles[i].IsAssigned)
                {
                    hospitalVehicles.Add(
                        hospital,
                        vehicles);
                    return;
                }
            }

            for (int i = 0; i < vehicles.Count; i++)
            {
                AmbulanceVehicleAgent agent = vehicles[i];
                if (agent == null)
                {
                    continue;
                }

                DestroyVehicle(agent);
            }
        }

        private static bool ContainsHospital(
            IReadOnlyList<Vector2Int> hospitals,
            Vector2Int hospital)
        {
            for (int i = 0; i < hospitals.Count; i++)
            {
                if (hospitals[i] == hospital)
                {
                    return true;
                }
            }

            return false;
        }

        private void DestroyVehicle(
            AmbulanceVehicleAgent agent)
        {
            if (agent == null)
            {
                return;
            }

            agent.Release();

            if (Application.isPlaying)
            {
                Destroy(agent.gameObject);
            }
            else
            {
                DestroyImmediate(agent.gameObject);
            }
        }
    }
}
