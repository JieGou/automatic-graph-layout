using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Core.Routing;
using Microsoft.Msagl.Core;

namespace Microsoft.Msagl.Routing.Rectilinear{
    ///<summary>
    ///</summary>
    public static class RectilinearInteractiveEditor {
        /// <summary>
        /// Create a RectilinearEdgeRouter from the passed obstacleNodes, with one port at the center of each obstacle,
        /// and route between the obstacles.
        /// </summary>
        /// <param name="cornerFitRadius">The radius of the arc inscribed into path corners</param>
        /// <param name="padding">The minimum padding from an obstacle's curve to its enclosing polyline</param>
        /// <param name="obstacleNodes">The nodes of the graph</param>
        /// <param name="geometryEdges">Edges defining the nodes to route between, and receiving the resultant paths</param>
        /// <param name="edgeRoutingMode">Mode of the edges (Rectilinear or RectilinearToCenter).</param>
        /// <param name="useSparseVisibilityGraph">Use a more memory-efficient but possibly path-suboptimal visibility graph</param>
        /// <param name="useObstacleRectangles">Use obstacle bounding box rectangles in visibility graph</param>
        /// <param name="bendPenaltyAsAPercentageOfDistance">The cost penalty for a bend in the path, as a percentage of the Manhattan distance
        ///             between the source target ports.</param>
        static public void CreatePortsAndRouteEdges(double cornerFitRadius, double padding
                            , IEnumerable<Node> obstacleNodes, IEnumerable<Edge> geometryEdges
                            , EdgeRoutingMode edgeRoutingMode, bool useSparseVisibilityGraph
                            , double bendPenaltyAsAPercentageOfDistance, double minEdgeSeparation, CancelToken ct = null) {
            var r = FillRouter(cornerFitRadius, padding, obstacleNodes, geometryEdges, edgeRoutingMode, useSparseVisibilityGraph
                            ,  bendPenaltyAsAPercentageOfDistance, minEdgeSeparation);
            r.Run(ct);
            CreateSelfEdges(geometryEdges.Where(e => e.SourcePort.Location == e.TargetPort.Location), cornerFitRadius);
        }

        /// <summary>
        /// Create a RectilinearEdgeRouter from the passed obstacleNodes, with one port at the center of each obstacle,
        /// and route between the obstacles, with default bend penalty.
        /// </summary>
        /// <param name="cornerFitRadius">The radius of the arc inscribed into path corners</param>
        /// <param name="padding">The minimum padding from an obstacle's curve to its enclosing polyline</param>
        /// <param name="obstacleNodes">The nodes of the graph</param>
        /// <param name="geometryEdges">Edges defining the nodes to route between, and receiving the resultant paths</param>
        /// <param name="edgeRoutingMode">Mode of the edges (Rectilinear or RectilinearToCenter).</param>
        /// <param name="useObstacleRectangles">Use obstacle bounding box rectangles in visibility graph</param>
        /// <param name="useSparseVisibilityGraph">Use a more memory-efficient but possibly path-suboptimal visibility graph</param>
        static public void CreatePortsAndRouteEdges(double cornerFitRadius, double padding
                            , IEnumerable<Node> obstacleNodes, IEnumerable<Edge> geometryEdges
                            , EdgeRoutingMode edgeRoutingMode, bool useSparseVisibilityGraph
                            , bool useObstacleRectangles, double minEdgeSeparation) {
            CreatePortsAndRouteEdges(cornerFitRadius, padding, obstacleNodes, geometryEdges, edgeRoutingMode
                            , useSparseVisibilityGraph,  SsstRectilinearPath.DefaultBendPenaltyAsAPercentageOfDistance, minEdgeSeparation);
        }

        /// <summary>
        /// Create a RectilinearEdgeRouter from the passed obstacleNodes, with one port at the center of each obstacle,
        /// and route between the obstacles, with default bend penalty.
        /// </summary>
        /// <param name="cornerFitRadius">The radius of the arc inscribed into path corners</param>
        /// <param name="padding">The minimum padding from an obstacle's curve to its enclosing polyline</param>
        /// <param name="obstacleNodes">The nodes of the graph</param>
        /// <param name="geometryEdges">Edges defining the nodes to route between, and receiving the resultant paths</param>
        /// <param name="edgeRoutingMode">Mode of the edges (Rectilinear or RectilinearToCenter).</param>
        /// <param name="useSparseVisibilityGraph">Use a more memory-efficient but possibly path-suboptimal visibility graph</param>
        static public void CreatePortsAndRouteEdges(double cornerFitRadius, double padding
                            , IEnumerable<Node> obstacleNodes, IEnumerable<Edge> geometryEdges
                            , EdgeRoutingMode edgeRoutingMode, bool useSparseVisibilityGraph, double minEdgeSeparation)
        {
            CreatePortsAndRouteEdges(cornerFitRadius, padding, obstacleNodes, geometryEdges, edgeRoutingMode
                            , useSparseVisibilityGraph, SsstRectilinearPath.DefaultBendPenaltyAsAPercentageOfDistance, minEdgeSeparation);
        }

        /// <summary>
        /// Create a RectilinearEdgeRouter populated with the passed obstacles.
        /// </summary>
        /// <returns>The populated RectilinearEdgeRouter</returns>
        static RectilinearEdgeRouter FillRouter(double cornerFitRadius, double padding, IEnumerable<Node> obstacleNodes, IEnumerable<Edge> geomEdges
                            , EdgeRoutingMode edgeRoutingMode, bool useSparseVisibilityGraph, double bendPenaltyAsAPercentageOfDistance, double minEdgeSeparation) {
            Debug.Assert((EdgeRoutingMode.Rectilinear == edgeRoutingMode) || (EdgeRoutingMode.RectilinearToCenter == edgeRoutingMode)
                        , "Non-rectilinear edgeRoutingMode");

            var nodeShapesMap = new Dictionary<Node, Shape>();
            FillNodeShapesMap(obstacleNodes, geomEdges, nodeShapesMap);
            var router = new RectilinearEdgeRouter(nodeShapesMap.Values, padding, cornerFitRadius, useSparseVisibilityGraph, minEdgeSeparation) {
                RouteToCenterOfObstacles = (edgeRoutingMode == EdgeRoutingMode.RectilinearToCenter),
                BendPenaltyAsAPercentageOfDistance = bendPenaltyAsAPercentageOfDistance
            };

            foreach (var geomEdge in geomEdges) {
                var edgeGeom = geomEdge.EdgeGeometry;
                edgeGeom.SourcePort = nodeShapesMap[geomEdge.Source].Ports.First();
                edgeGeom.TargetPort = nodeShapesMap[geomEdge.Target].Ports.First();
                router.AddEdgeGeometryToRoute(edgeGeom);
            }
            return router;
        }

        private static void FillNodeShapesMap(IEnumerable<Node> obstacleNodes, IEnumerable<Edge> geomEdges, Dictionary<Node, Shape> nodeShapeMap) {
            foreach (Node node in obstacleNodes) {
                Shape shape = CreateShapeWithRelativeNodeAtCenter(node);
                nodeShapeMap.Add(node, shape);
            }

            foreach (var e in geomEdges) {
                var node = e.Source;
                if (!nodeShapeMap.ContainsKey(node)) {

                    nodeShapeMap.Add(node, CreateShapeWithRelativeNodeAtCenter(node));
                }
                node = e.Target;
                if (!nodeShapeMap.ContainsKey(node)) {

                    nodeShapeMap.Add(node, CreateShapeWithRelativeNodeAtCenter(node));
                }
            }
        }

        static void CreateSelfEdges(IEnumerable<Edge> selfEdges, double cornerFitRadius) {
            foreach (var edge in selfEdges) {
                CreateSimpleEdgeCurveWithGivenFitRadius(edge, cornerFitRadius);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="edge"></param>
        /// <param name="cornerFitRadius"></param>
        public static void CreateSimpleEdgeCurveWithGivenFitRadius(Edge edge, double cornerFitRadius)
        {
            ValidateArg.IsNotNull(edge, "edge");
            var a = edge.Source.Center;
            var b = edge.Target.Center;
            if (edge.Source == edge.Target)
            {
                var dx = edge.Source.BoundaryCurve.BoundingBox.Width / 2;
                var dy = edge.Source.BoundingBox.Height / 4;
                edge.UnderlyingPolyline = CreateUnderlyingPolylineForSelfEdge(a, dx, dy);
                for (var site = edge.UnderlyingPolyline.HeadSite.Next; site.Next != null; site = site.Next) CalculateCoefficiensUnderSite(site, cornerFitRadius);
                edge.Curve = edge.UnderlyingPolyline.CreateCurve();
            }
            else
            {
                edge.UnderlyingPolyline = SmoothedPolyline.FromPoints(new[] { a, b });
                edge.Curve = edge.UnderlyingPolyline.CreateCurve();
            }

            if (!Arrowheads.TrimSplineAndCalculateArrowheads(edge.EdgeGeometry, edge.Source.BoundaryCurve, edge.Target.BoundaryCurve, edge.Curve,
                true))
                Arrowheads.CreateBigEnoughSpline(edge);
        }



        /// <summary>
        /// creates an edge curve based only on the source and target geometry
        /// </summary>
        /// <param name="edge"></param>
        public static void CreateSimpleEdgeCurve(Edge edge)
        {
            ValidateArg.IsNotNull(edge, "edge");
            var a = edge.Source.Center;
            var b = edge.Target.Center;
            if (edge.Source == edge.Target)
            {
                var dx = edge.Source.BoundaryCurve.BoundingBox.Width / 2;
                var dy = edge.Source.BoundingBox.Height / 4;
                edge.UnderlyingPolyline = CreateUnderlyingPolylineForSelfEdge(a, dx, dy);
                edge.Curve = edge.UnderlyingPolyline.CreateCurve();
            }
            else
            {
                edge.UnderlyingPolyline = SmoothedPolyline.FromPoints(new[] { a, b });
                edge.Curve = edge.UnderlyingPolyline.CreateCurve();
            }

            if (!Arrowheads.TrimSplineAndCalculateArrowheads(edge.EdgeGeometry, edge.Source.BoundaryCurve, edge.Target.BoundaryCurve, edge.Curve, true))
                Arrowheads.CreateBigEnoughSpline(edge);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1806:DoNotIgnoreMethodResults", MessageId = "Microsoft.Msagl.Core.Geometry.Site")]
        static SmoothedPolyline CreateUnderlyingPolylineForSelfEdge(Point p0, double dx, double dy)
        {
            var p1 = p0 + new Point(0, dy);
            var p2 = p0 + new Point(dx, dy);
            var p3 = p0 + new Point(dx, -dy);
            var p4 = p0 + new Point(0, -dy);

            var site = new CornerSite(p0);
            var polyline = new SmoothedPolyline(site);
            site = new CornerSite(site, p1);
            site = new CornerSite(site, p2);
            site = new CornerSite(site, p3);
            site = new CornerSite(site, p4);
            new CornerSite(site, p0);
            return polyline;
        }

        /// <summary>
        /// Create a Shape with a single relative port at its center.
        /// </summary>
        /// <param name="node">The node from which the shape is derived</param>
        /// <returns></returns>
        public static Shape CreateShapeWithRelativeNodeAtCenter(Node node) {
            ValidateArg.IsNotNull(node, "node");
            var shape = new RelativeShape(() => node.BoundaryCurve);
            shape.Ports.Insert(new RelativeFloatingPort(() => node.BoundaryCurve, () => node.Center));
            return shape;
        }

        private static void CalculateCoefficiensUnderSite(CornerSite site, double radius){
            double l = radius/(site.Point - site.Previous.Point).Length;
            l = Math.Min(0.5, l);
            site.PreviousBezierSegmentFitCoefficient = l;
            l = radius / (site.Next.Point-site.Point).Length;
            l = Math.Min(0.5, l);
            site.NextBezierSegmentFitCoefficient = l;
        }
    }
}
