using System;
using Autodesk.Revit.DB;

namespace HCB.RevitAddin
{
    internal static class MepConnectorHelper
    {
        public const double PositionTolerance = 1e-4;

        public static ConnectorManager? GetConnectorManager(Element? element)
        {
            return element switch
            {
                MEPCurve curve => curve.ConnectorManager,
                FamilyInstance { MEPModel: not null } familyInstance => familyInstance.MEPModel.ConnectorManager,
                _ => null
            };
        }

        public static bool HasPhysicalConnectors(Element? element)
        {
            return GetPhysicalConnectors(element).Any();
        }

        public static IEnumerable<Connector> GetPhysicalConnectors(Element? element)
        {
            ConnectorManager? manager = GetConnectorManager(element);
            if (manager == null)
            {
                yield break;
            }

            foreach (Connector connector in manager.Connectors)
            {
                if (connector.ConnectorType == ConnectorType.End)
                {
                    yield return connector;
                }
            }
        }

        public static List<Connector> GetConnectedPartners(Connector connector)
        {
            List<Connector> partners = [];
            if (!connector.IsConnected)
            {
                return partners;
            }

            foreach (Connector reference in connector.AllRefs)
            {
                if (reference.Owner == null ||
                    reference.Owner.Id == connector.Owner.Id ||
                    reference.ConnectorType == ConnectorType.Logical ||
                    reference.Owner is MEPSystem)
                {
                    continue;
                }

                partners.Add(reference);
            }

            return partners;
        }

        /// <summary>
        /// Collects the element and everything physically connected to it, directly or through other elements.
        /// Returns false when the network grows beyond <paramref name="limit"/> elements.
        /// </summary>
        public static bool TryCollectConnectedGroup(Element start, int limit, out HashSet<ElementId> group)
        {
            group = [start.Id];
            Queue<Element> queue = new();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Element current = queue.Dequeue();
                ConnectorManager? manager = GetConnectorManager(current);
                if (manager == null)
                {
                    continue;
                }

                foreach (Connector connector in manager.Connectors)
                {
                    if (connector.ConnectorType == ConnectorType.Logical)
                    {
                        continue;
                    }

                    foreach (Connector partner in GetConnectedPartners(connector))
                    {
                        if (group.Add(partner.Owner.Id))
                        {
                            if (group.Count > limit)
                            {
                                return false;
                            }

                            queue.Enqueue(partner.Owner);
                        }
                    }
                }
            }

            return true;
        }

        public static Connector? FindClosestConnector(Element element, XYZ? point, bool preferFree)
        {
            List<Connector> connectors = GetPhysicalConnectors(element).ToList();
            if (connectors.Count == 0)
            {
                return null;
            }

            IEnumerable<Connector> candidates = connectors;
            if (preferFree)
            {
                List<Connector> free = connectors.Where(connector => !connector.IsConnected).ToList();
                if (free.Count > 0)
                {
                    candidates = free;
                }
            }

            if (point == null)
            {
                return candidates.First();
            }

            return candidates
                .OrderBy(connector => connector.Origin.DistanceTo(point))
                .First();
        }

        public static Connector? FindConnectorAt(Element element, XYZ origin)
        {
            return GetPhysicalConnectors(element)
                .OrderBy(connector => connector.Origin.DistanceTo(origin))
                .FirstOrDefault(connector => connector.Origin.DistanceTo(origin) < 0.01);
        }

        public static XYZ GetDirection(Connector connector)
        {
            return connector.CoordinateSystem.BasisZ.Normalize();
        }

        public static bool AreOpposite(XYZ first, XYZ second)
        {
            return first.Normalize().DotProduct(second.Normalize()) < -1d + 1e-6;
        }

        public static XYZ GetAnyPerpendicular(XYZ direction)
        {
            XYZ normalized = direction.Normalize();
            XYZ candidate = Math.Abs(normalized.DotProduct(XYZ.BasisZ)) < 0.9 ? XYZ.BasisZ : XYZ.BasisX;
            return normalized.CrossProduct(candidate).Normalize();
        }

        public static string Describe(Element element)
        {
            string category = element.Category?.Name ?? "Element";
            return $"{category} {element.Id.Value}";
        }
    }
}
