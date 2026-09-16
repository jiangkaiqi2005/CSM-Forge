using System;
using System.Collections.Generic;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private readonly Dictionary<ushort, Hash256> clientProjectionExpected = new Dictionary<ushort, Hash256>();
        private IReplicaDomainV2[] clientProjectionDomains;
        private int clientProjectionCursor;
        private int clientProjectionTicks;
        private ulong clientProjectionRevision;

        private IReplicaDomainV2[] GetClientProjectionDomains()
        {
            return new IReplicaDomainV2[]
            {
                clientWater, clientDemand, clientTaxes, clientBudgets, clientCash, clientEconomyControl,
                clientAreas, clientBuildings, clientNet, clientZones, clientDistricts, clientClock,
                clientTransport, clientNames, clientCityName, clientWeather
            };
        }

        private void InitializeClientProjectionAudit()
        {
            clientProjectionExpected.Clear();
            clientProjectionDomains = null;
            clientProjectionCursor = 0;
            clientProjectionTicks = 0;
            clientProjectionRevision = replica == null ? 0 : replica.Revision;
            if (replica == null) return;

            IReplicaDomainV2[] domains = GetClientProjectionDomains();
            for (int i = 0; i < domains.Length; i++)
            {
                IReplicaDomainV2 domain = domains[i];
                if (domain == null || domain.DomainId == 0 || domain.StateRoot == null)
                    throw new InvalidOperationException("Replica projection audit cannot initialize an incomplete domain.");
                if (clientProjectionExpected.ContainsKey(domain.DomainId))
                    throw new InvalidOperationException("Replica projection audit saw a duplicate domain id.");
                clientProjectionExpected.Add(domain.DomainId, domain.StateRoot);
            }
            clientProjectionDomains = domains;
        }

        private void ClearClientProjectionAudit()
        {
            clientProjectionExpected.Clear();
            clientProjectionDomains = null;
            clientProjectionCursor = 0;
            clientProjectionTicks = 0;
            clientProjectionRevision = 0;
        }

        private void ObserveClientProjectionBatch(AuthorityBatch batch)
        {
            if (batch == null || clientProjectionDomains == null) return;
            clientProjectionExpected[batch.DomainId] = batch.AfterRoot;
            clientProjectionRevision = batch.Revision;
        }

        internal void AuditClientProjection()
        {
            if (mode != MultiplayerSessionMode.ClientLive || replica == null || clientProjectionDomains == null ||
                clientProjectionDomains.Length == 0) return;
            if (++clientProjectionTicks < 256) return;
            clientProjectionTicks = 0;

            if (clientProjectionRevision != replica.Revision)
            {
                // Diagnostic state should never lag an applied batch. Re-seed rather than changing gameplay state.
                events.Record(RuntimeEventCode.Error, load.Generation,
                    "diagnostic-only projection audit revision lag: expected=" + clientProjectionRevision + ", actual=" + replica.Revision);
                InitializeClientProjectionAudit();
                return;
            }

            IReplicaDomainV2 domain = clientProjectionDomains[clientProjectionCursor++ % clientProjectionDomains.Length];
            Hash256 expected;
            if (domain == null || !clientProjectionExpected.TryGetValue(domain.DomainId, out expected) || expected == null)
            {
                events.Record(RuntimeEventCode.Error, load.Generation,
                    "diagnostic-only projection audit missing domain expectation");
                return;
            }

            try
            {
                Hash256 actual = CaptureActualProjectionRoot(domain);
                if (actual == null || expected.Equals(actual)) return;
                string detail = "diagnostic-only projection drift: domain=" + domain.DomainId +
                    ", revision=" + replica.Revision + ", expected=" + ShortHash(expected) + ", actual=" + ShortHash(actual);
                events.Record(RuntimeEventCode.Error, load.Generation, detail);
                UnityEngine.Debug.LogWarning("[CSM-Forge] " + detail + "; no automatic resync was triggered.");
            }
            catch (Exception error)
            {
                events.Record(RuntimeEventCode.Error, load.Generation,
                    "diagnostic-only projection audit failed: domain=" + domain.DomainId + ", error=" + error.GetType().Name);
            }
        }

        private static Hash256 CaptureActualProjectionRoot(IReplicaDomainV2 domain)
        {
            if (domain.DomainId == EconomyCashAuthorityDomain.Id)
                return EconomyCashGameAccess.Capture().Root;
            if (domain.DomainId == EconomyControlAuthorityDomain.Id)
                return EconomyControlGameAccess.Capture().Root;
            return domain.StateRoot;
        }

        private static string ShortHash(Hash256 value)
        {
            if (value == null) return "null";
            string text = value.ToString();
            return text.Length <= 16 ? text : text.Substring(0, 16);
        }
    }
}
