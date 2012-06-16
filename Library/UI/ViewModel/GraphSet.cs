﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace XLibrary
{
    public class GraphSet
    {
        ViewModel Model;
        CallGraphMode GraphMode;

        // a single graph area may be divided for independent graphs
        public List<Graph> Graphs = new List<Graph>();

        public Dictionary<int, NodeModel> PositionMap = new Dictionary<int, NodeModel>();
        public HashSet<int> CenterMap = new HashSet<int>(); // used to filter calls into and out of center

        // sub sets are entire graphs within a subnode in the current graph 
        public List<GraphSet> Subsets = new List<GraphSet>();

        public NodeModel GraphContainer;


        public GraphSet(ViewModel model, NodeModel root, NodeModel container=null, int depth = -1)
        {
            Model = model;
            GraphMode = Model.GraphMode;
            GraphContainer = container;

            // iternate nodes at this zoom level
            if (GraphMode == CallGraphMode.Intermediates)
            {
                AddDependencyNodes();
            }

            else if (GraphMode == CallGraphMode.Layers)
            {
                foreach (var child in root.Nodes)
                    AddCalledNodes(child, true, 0);
            }

            else
            {
                AddCalledNodes(root, true, depth);

                if (GraphContainer == null)
                {
                    if (Model.ShowOutside || CenterMap.Count == 1) // prevent blank screen
                        AddCalledNodes(Model.InternalRoot, false);

                    if (Model.ShowExternal)
                        AddCalledNodes(Model.ExternalRoot, false);
                }
            }

            if (PositionMap.Count > 0)
            {
                BuildGraphs();

                if (Graphs.Count > 0)
                    LayoutGraphs();
            }


            if (GraphMode == CallGraphMode.Layers)
            {
                foreach (var child in root.Nodes)
                {
                    var set = new GraphSet(Model, child, child);
                    Subsets.Add(set);
                }
            }

            else if ((GraphMode == CallGraphMode.Class || GraphMode == CallGraphMode.Init) &&
                     Model.ShowMethods &&
                     GraphContainer == null)
            {
                var classNodes = PositionMap.Values.ToArray();

                foreach (var classNode in classNodes)
                {
                    var set = new GraphSet(Model, classNode, classNode, 1);
                    Subsets.Add(set);
                }
            }
        }

        public void AddCalledNodes(NodeModel node, bool center, int depth = -1)
        {
            if (!node.Show)
                return;

            node.Intermediates = null;

            var xNode = node.XNode;

            if (node.XNode.IsAnon && !Model.ShowAnon)
            {
                // ignore node
            }

            else if (GraphMode == CallGraphMode.Dependencies &&
                     ((xNode.Independencies != null && xNode.Independencies.Length > 0) ||
                      (xNode.Dependencies != null && xNode.Dependencies.Length > 0)))
            {
                if (center)
                    CenterMap.Add(node.ID);

                // if not center then only add if connected to center, center=false called on second pass so centerMap is totally initd
                if (center || Model.DependentClasses.Contains(node.ID) || Model.IndependentClasses.Contains(node.ID))
                {
                    PositionMap[node.ID] = node;

                    if (xNode.Independencies != null)
                        node.EdgesIn = xNode.Independencies;

                    if (xNode.Dependencies != null)
                        node.EdgesOut = xNode.Dependencies;
                }
            }
            else if (GraphMode == CallGraphMode.Layers)
            {
                if (center)
                    CenterMap.Add(node.ID);

                PositionMap[node.ID] = node;

                if (xNode.LayerIn != null)
                    node.EdgesIn = xNode.LayerIn.ToArray();

                if (xNode.LayerOut != null)
                    node.EdgesOut = xNode.LayerOut.ToArray();
            }
            else if (GraphMode == CallGraphMode.Init && node.ObjType == XObjType.Class && GraphContainer == null)
            {
                AddEdges(node, center, xNode.InitsBy, xNode.InitsOf);
            }
            else if ((GraphMode == CallGraphMode.Class && node.ObjType == XObjType.Class && GraphContainer == null) ||
                     (GraphMode == CallGraphMode.Method && node.ObjType != XObjType.Class) ||
                     (GraphContainer != null && node.ObjType != XObjType.Class))
            {
                AddEdges(node, center, xNode.CalledIn, xNode.CallsOut);
            }

            if (depth == 0)
                return;

            depth--;

            foreach (var sub in node.Nodes)
                if (sub != Model.InternalRoot) // when traversing outside root, dont interate back into center root
                    AddCalledNodes(sub, center, depth);
        }

        private void AddEdges(NodeModel node, bool center, SharedDictionary<FunctionCall> callsIn, SharedDictionary<FunctionCall> callsOut)
        {
            if ((callsIn == null || callsIn.Length == 0) &&
                (callsOut == null || callsOut.Length == 0))
                return;

            if (center)
                CenterMap.Add(node.ID);

            // if not center then only add if connected to center, center=false called on second pass so centerMap is totally initd
            if (center ||
                 (callsIn != null && callsIn.Any(c => CenterMap.Contains(c.Source))) ||
                 (callsOut != null && callsOut.Any(c => CenterMap.Contains(c.Destination))))
            {
                PositionMap[node.ID] = node;

                if (callsIn != null)
                    node.EdgesIn = callsIn.Select(c => c.Source).ToArray();

                if (callsOut != null)
                    node.EdgesOut = callsOut.Select(c => c.Destination).ToArray();
            }
        }

        private void LayoutGraphs()
        {
            Graphs = Graphs.OrderByDescending(g => g.Weight).ToList();


            float totalWeight = Graphs.Sum(g => g.Weight);
            float totalArea = 1; // unit scalable area

            float weightToPix = totalArea / totalWeight * 0.5f; // reduction factor

            Graphs.ForEach(g =>
            {
                foreach (var n in g.Nodes())
                    n.ScaledSize = (float)Math.Sqrt(n.Value * weightToPix);
            });


            // sum the heights of the biggest ranks of each graph
            float[] maxRankHeights = Graphs.Select(g => g.Ranks.Max(rank => rank.Column.Sum(n => n.ScaledSize))).ToArray();
            float stackHeight = maxRankHeights.Sum();

            float heightReduce = 1 / stackHeight;

            // check x axis reduce, and if less use that one
            // do for all graphs at once so proportions stay the same

            // find graph with max width, buy summing max node in each rank
            float[] maxRankWidths = Graphs.Select(g => g.Ranks.Sum(rank => rank.Column.Max(n => n.ScaledSize))).ToArray();

            float widthReduce = 1 / maxRankWidths.Max(); ;

            float reduce = (float)(Math.Min(heightReduce, widthReduce) * 0.75);

            Graphs.ForEach(g =>
            {
                foreach (var n in g.Nodes())
                    n.ScaledSize *= reduce;
            });


            // give each group a height proportional to their max rank height
            float groupOffset = 0;

            for (int i = 0; i < Graphs.Count; i++)
            {
                var graph = Graphs[i];

                graph.ScaledHeight = maxRankHeights[i] / stackHeight;
                graph.ScaledOffset = groupOffset;

                float xOffset = graph.Ranks[0].Column.Max(n => n.ScaledSize) / 2f; // 0.02f;
                float freespace = 1 - xOffset;// -maxRankWidths[i] * reduce;

                float spaceX = freespace / graph.Ranks.Length;


                for (int x = 0; x < graph.Ranks.Length; x++)
                {
                    var rank = graph.Ranks[x];

                    //float maxSize = rank.Column.Max(n => n.ScaledSize);

                    // a good first guess at how nodes should be ordered to min crossing
                    //rank.Column = rank.Column.OrderBy(n => n.XNode.HitSequence).ToList();

                    PositionRank(graph, rank, xOffset);// + maxSize / 2);

                    xOffset += spaceX;
                }

                groupOffset += graph.ScaledHeight;

                if (Model.SequenceOrder)
                {
                    for (int x = 0; x < 6; x++)
                        MinDistance(graph);

                    //Spring(graph);
                }
                else
                {
                    for (int x = 0; x < 3; x++)
                        Uncross(graph);

                    for (int x = 0; x < 3; x++)
                        MinDistance(graph);

                    for (int x = 0; x < 3; x++)
                        Uncross(graph);

                    for (int x = 0; x < 3; x++)
                        MinDistance(graph);
                }
            }
        }

        private void BuildGraphs()
        {
            // group nodes in position map into graphs
            foreach (var node in PositionMap.Values)
                node.Rank = null;

            do
            {
                // group nodes into connected graphs
                var graph = new Dictionary<int, NodeModel>();

                // add first unranked node to a graph
                var unrankedNode = PositionMap.Values.First(n => n.Rank == null);

                LayoutGraph(graph, unrankedNode, 0, new List<int>());

                // while group contains unranked nodes
                while (graph.Values.Any(n => n.Rank == null && n.EdgesOut != null))
                {
                    // head node to start traversal
                    unrankedNode = graph.Values.First(n => n.Rank == null && n.EdgesOut != null);

                    // only way node could be in group is if child added it, so there is a minrank
                    // min rank is 1 back from the lowest ranked child of the node
                    int? minRank = unrankedNode.EdgesOut.Min(dest =>
                    {
                        if (PositionMap.ContainsKey(dest))
                        {
                            var destNode = PositionMap[dest];
                            if (destNode.Rank.HasValue)
                                return destNode.Rank.Value;
                        }

                        return int.MaxValue;
                    });

                    LayoutGraph(graph, unrankedNode, minRank.Value - 1, new List<int>());//, new List<string>());
                }


                // remove graphs with 1 element
                if (graph.Count == 1 && !Model.ShowMethods && GraphMode != CallGraphMode.Layers)
                {
                    var remove = graph.Values.First();
                    PositionMap.Remove(remove.ID);
                    CenterMap.Remove(remove.ID);

                    continue;
                }

                // normalize ranks so sequential without any missing between
                int nextSequentialRank = -1;
                int currentRank = int.MinValue;
                foreach (var n in graph.Values.OrderBy(v => v.Rank))
                {
                    if (n.Rank != currentRank)
                    {
                        currentRank = n.Rank.Value;
                        nextSequentialRank++;
                    }

                    n.Rank = nextSequentialRank;
                }

                // put all nodes into a rank based multi-map
                Rank[] ranks = new Rank[nextSequentialRank + 1];
                for (int i = 0; i < ranks.Length; i++)
                    ranks[i] = new Rank();

                long graphWeight = 0;

                foreach (var source in graph.Values)
                {
                    graphWeight += source.Value;


                    ranks[source.Rank.Value].Column.Add(source);

                    if (source.EdgesOut == null)
                        continue;

                    foreach (var destId in source.EdgesOut)
                    {
                        if (!graph.ContainsKey(destId))
                            continue;

                        var destination = graph[destId];

                        // ranks are equal if nodes are outside zoom
                        if (source.ID == destination.ID || destination.Rank == source.Rank)
                            continue;

                        if (source.Intermediates != null)
                            source.Intermediates.Remove(destId);

                        // if destination is not 1 forward/1 back then create intermediate nodes
                        if (source.Rank != destination.Rank + 1 &&
                            source.Rank != destination.Rank - 1)
                        {
                            if (source.Intermediates == null)
                                source.Intermediates = new Dictionary<int, List<NodeModel>>();

                            source.Intermediates[destId] = new List<NodeModel>();

                            bool increase = destination.Rank > source.Rank;
                            int nextRank = increase ? source.Rank.Value + 1 : source.Rank.Value - 1;
                            var lastNode = source;

                            while (nextRank != destination.Rank)
                            {

                                // create new node
                                var intermediate = new NodeModel(Model);
                                intermediate.Rank = nextRank;
                                intermediate.Value = 10; // todo make smarter - 
                                intermediate.Adjacents = new List<NodeModel>();

                                // add forward node to prev
                                if (lastNode != source)
                                    lastNode.Adjacents.Add(intermediate);

                                // add back node to curr
                                intermediate.Adjacents.Add(lastNode);

                                // add to temp path, rank map
                                source.Intermediates[destId].Add(intermediate);
                                ranks[nextRank].Column.Add(intermediate);
                                //PositionMap not needed because we dont need any mouse over events? just follow along and draw from list, not id

                                lastNode = intermediate;
                                nextRank = increase ? nextRank + 1 : nextRank - 1;
                            }


                            try
                            {
                                lastNode.Adjacents.Add(destination);
                                source.Intermediates[destId].Add(destination);
                            }
                            catch
                            {
                                System.IO.File.WriteAllText("debugX.txt", string.Format("{0}\r\n{1}\r\n", source.Rank, destination.Rank));

                                throw new Exception("wtf");
                            }
                        }
                    }
                }

                Graphs.Add(new Graph() { Ranks = ranks, Weight = graphWeight });

            } while (PositionMap.Values.Any(n => n.Rank == null));

        }

        private void Uncross(Graph graph)
        {
            // moves nodes closer to attached nodes in adjacent ranks

            foreach (Rank rank in graph.Ranks)
            {
                // foreach node average y pos form all connected edges
                foreach (var node in rank.Column)
                    node.ScaledLocation.Y = AvgPos(node);

                // set rank list to new node list
                rank.Column = rank.Column.OrderBy(n => n.ScaledLocation.Y).ToList();

                PositionRank(graph, rank, rank.Column[0].ScaledLocation.X);
            }
        }

        private float AvgPos(NodeModel node)
        {
            float sum = 0;
            float count = 0;

            if (node.EdgesOut != null)
                foreach (var destId in node.EdgesOut)
                    if (PositionMap.ContainsKey(destId))
                    {
                        if (node.Intermediates != null && node.Intermediates.ContainsKey(destId))
                            sum += node.Intermediates[destId][0].ScaledLocation.Y;
                        else
                            sum += PositionMap[destId].ScaledLocation.Y;

                        count++;
                    }

            if (node.EdgesIn != null)
                foreach (var source in node.EdgesIn)
                    if (PositionMap.ContainsKey(source))
                    {
                        var sourceNode = PositionMap[source];
                        if (sourceNode.Intermediates != null && sourceNode.Intermediates.ContainsKey(node.ID))
                            sum += sourceNode.Intermediates[node.ID].Last().ScaledLocation.Y;
                        else
                            sum += PositionMap[source].ScaledLocation.Y;

                        count++;
                    }

            // should only be attached to intermediate nodes
            if (node.Adjacents != null)
            {
                Debug.Assert(node.ID == 0); // adjacents should only be on temp nodes

                foreach (var adj in node.Adjacents)
                {
                    sum += adj.ScaledLocation.Y;
                    count++;
                }
            }

            if (count == 0)
                return node.ScaledLocation.Y;

            return sum / count;
        }

        private void Spring(Graph graph)
        {
            float total_kinetic_energy = 40; // running sum of total kinetic energy over all particles

            float spring_const = -1;
            float damping = 0.8f; // between 0 and 1
            float timestep = 0.1f;

            do
            {
                // for each node
                foreach (var rank in graph.Ranks)
                {
                    float minY = 0;
                    float maxY = 1;

                    foreach (var node in rank.Column)
                    {
                        float forceY = 0; // running sum of total force on this particular node


                        // for each other node apply Coulomb repulsion 1/distance
                        foreach (var other_node in rank.Column.Where(n => n != node))
                        {
                            float distance = GetDistanceY(other_node, node);
                            if (distance > 0)
                                forceY += 1.0f / distance;
                            else
                                forceY += .1f;
                        }

                        // for each spring connected to this node net-force + Hooke_attraction( this_node, spring )

                        if (node.EdgesOut != null)
                            foreach (var destId in node.EdgesOut)
                                if (PositionMap.ContainsKey(destId))
                                {
                                    if (node.Intermediates != null && node.Intermediates.ContainsKey(destId))
                                        forceY += -spring_const * GetDistanceY(node.Intermediates[destId][0], node);
                                    else
                                        forceY += -spring_const * GetDistanceY(PositionMap[destId], node);
                                }

                        if (node.EdgesIn != null)
                            foreach (var source in node.EdgesIn)
                                if (PositionMap.ContainsKey(source))
                                {
                                    var sourceNode = PositionMap[source];
                                    if (sourceNode.Intermediates != null && sourceNode.Intermediates.ContainsKey(node.ID))
                                        forceY += -spring_const * GetDistanceY(sourceNode.Intermediates[node.ID].Last(), node);
                                    else
                                        forceY += -spring_const * GetDistanceY(PositionMap[source], node);
                                }

                        // should only be attached to intermediate nodes
                        if (node.Adjacents != null)
                            foreach (var adj in node.Adjacents)
                                forceY += -spring_const * GetDistanceY(adj, node);

                        // without damping, it moves forever
                        node.VelocityY = (node.VelocityY + timestep * forceY) * damping;
                        node.ScaledLocation.Y += timestep * node.VelocityY;

                        if (node.ScaledLocation.Y < minY)
                            minY = node.ScaledLocation.Y;
                        if (node.ScaledLocation.Y > maxY)
                            maxY = node.ScaledLocation.Y;

                        total_kinetic_energy--; // += node.Value * (float)Math.Pow(node.VelocityY, 2); // node.value is mass
                    }


                    // scale positions of nodes back
                    float range = maxY - minY;

                    if (range > 1)
                        foreach (var node in rank.Column)
                            node.ScaledLocation.Y /= range;
                }

            } while (total_kinetic_energy > 0); // the simulation has stopped moving

        }

        private float GetDistanceY(NodeModel other_node, NodeModel node)
        {
            return Math.Abs(other_node.ScaledLocation.Y - node.ScaledLocation.Y);
        }

        private void MinDistance(Graph graph)
        {
            // moves nodes with-in rank closer to their adjacent nodes without changing order in rank
            try
            {
                foreach (Rank rank in graph.Ranks)
                {
                    var nodes = rank.Column;

                    // foreach node average y pos form all connected edges
                    for (int x = 0; x < nodes.Count; x++)
                    {
                        var node = nodes[x];

                        float halfSize = node.ScaledSize / 2;

                        float lowerbound = (x > 0) ? nodes[x - 1].ScaledLocation.Y + (nodes[x - 1].ScaledSize / 2) : 0;
                        lowerbound += rank.MinHeightSpace;

                        float upperbound = (x < nodes.Count - 1) ? nodes[x + 1].ScaledLocation.Y - (nodes[x + 1].ScaledSize / 2) : float.MaxValue;
                        upperbound -= rank.MinHeightSpace;

                        //Debug.Assert(lowerbound <= upperbound);
                        if (lowerbound >= upperbound)
                        {
                            // usually if this happens they're very close
                            XRay.LogError("lower bound greater than upper in layout. pos: {0}, nodeID: {1}, lower: {2}, upper: {3}, minheight: {4}", x, node.ID, lowerbound, upperbound, rank.MinHeightSpace);
                            //continue;
                        }


                        float optimalY = AvgPos(node);

                        if (optimalY - halfSize < lowerbound)
                            optimalY = lowerbound + halfSize;

                        else if (optimalY + halfSize > upperbound)
                            optimalY = upperbound - halfSize;

                        node.ScaledLocation.Y = optimalY;
                    }
                }
            }
            catch (Exception ex)
            {
                XRay.LogError(ex.Message + "\r\n" + ex.StackTrace);
            }
        }

        private void PositionRank(Graph graph, Rank rank, float xOffset)
        {
            // spreads nodes in rank across y-axis at even intervals

            var nodes = rank.Column;

            float totalHeight = nodes.Sum(n => n.ScaledSize);

            float freespace = 1 * graph.ScaledHeight - totalHeight;

            float ySpace = freespace / (nodes.Count + 1); // space between each block
            rank.MinHeightSpace = ySpace;// (float)Math.Min(ySpace, 4.0 / (float)Height);
            float yOffset = ySpace;

            foreach (var node in nodes)
            {
                node.ScaledLocation.X = xOffset;
                node.ScaledLocation.Y = 1 * graph.ScaledOffset + yOffset + node.ScaledSize / 2;

                yOffset += node.ScaledSize + ySpace;
            }
        }

        /*
        flow through entire list of children first
	        keep track of node parents
		        if any unranked parents at end of child run
			        look at all of parents children, give rank of 1 - lowest ranked child
				        re-run alg on that parent
        				
	        this way we start with first (entry) node, run through linearly from that, and tack on alternate parents later
		        works great for application call graph, and for general graphs as well
         */
        private void LayoutGraph(Dictionary<int, NodeModel> graph, NodeModel node, int minRank, List<int> parents)
        {
            //debugLog.Add(string.Format("Entered Node ID {0} rank {1}", ID, Rank));

            // node already ranked correctly, no need to re-rank subordinates
            if (node.Rank != null && node.Rank.Value >= minRank)
                return;

            int? prevRank = node.Rank;

            // only increase rank
            Debug.Assert(node.Rank == null || minRank > node.Rank.Value);
            node.Rank = minRank;

            //debugLog.Add(string.Format("Node ID {0} rank set from {1} to {2}", ID, prevRank, Rank));

            parents.Add(node.ID);
            graph[node.ID] = node;

            if (node.EdgesOut != null)
                foreach (var destId in node.EdgesOut)
                {
                    if (parents.Contains(destId))
                    {
                        // destination rank should be less than source
                        //Debug.Assert(edge.Destination.Rank < edge.Source.Rank);

                        //debugLog.Add(string.Format("Switching edge {0} -> {1}, rank {2} -> {3}", ID, edge.Destination.ID, Rank, edge.Destination.Rank));

                        //edge.Source = edge.Destination;
                        //edge.Destination = this;
                        //edge.Reversed = !edge.Reversed;

                        continue;
                    }

                    // pass copy of parents list so that sub can add elemenets without affecting next iteration
                    //debugLog.Add(string.Format("Traversing to child {0} -> {1}, rank {2} -> {3}", ID, edge.Destination.ID, Rank, edge.Destination.Rank));

                    if (PositionMap.ContainsKey(destId))
                    {
                        var target = PositionMap[destId];

                        LayoutGraph(graph, target, node.Rank.Value + 1, parents.ToList());//, debugLog);
                    }
                    //debugLog.Add(string.Format("Return to node {0} rank {1}", ID, Rank));
                }

            // record so later group can be traversed for null ranked members (parents) so layout can be run on them
            if (node.EdgesIn != null)
                foreach (var source in node.EdgesIn)
                    if (PositionMap.ContainsKey(source))
                        graph[source] = PositionMap[source];

            // check if same edges down go back up and create intermediates in that case?

            //debugLog.Add(string.Format("Exited Node ID {0} rank {1}", ID, Rank));
        }

        private void AddDependencyNodes()
        {
            foreach (var n in Model.NodeModels)
            {
                n.Intermediates = null;
                n.DependencyChainIn = null;
                n.DependencyChainOut = null;
            }

            foreach (var n in Model.InterDependencies.Values)
            {
                CenterMap.Add(n.ID);
                PositionMap[n.ID] = n;

                var find = Model.InterDependencies.Keys.ToList();
                find.Remove(n.ID);

                FindChainTo(n, find);
                //FindIntermediatesFrom(n); // creates too many interdependent links, gets confusing
            }

            foreach (var n in Model.NodeModels)
            {
                if (n.DependencyChainIn != null)
                    n.EdgesIn = n.DependencyChainIn.Keys.ToArray();

                if (n.DependencyChainOut != null)
                    n.EdgesOut = n.DependencyChainOut.Keys.ToArray();
            }
        }

        public bool FindChainTo(NodeModel n, List<int> find, HashSet<int> traversed = null)
        {
            if (traversed == null)
                traversed = new HashSet<int>();

            if (traversed.Contains(n.ID))
                return false;

            traversed.Add(n.ID);

            bool pathFound = false;

            if (n.XNode.Dependencies == null)
                return false;

            foreach (var d in n.XNode.Dependencies)
            {
                var sub = Model.NodeModels[d];
                bool addLink = false;

                if (find.Contains(d))
                {
                    addLink = true;
                    find.Remove(d);
                }

                if (find.Count > 0 && FindChainTo(sub, find, traversed))
                    addLink = true;

                if (addLink)
                {
                    PositionMap[sub.ID] = sub;

                    n.AddIntermediateDependency(sub);
                    pathFound = true;
                }
            }

            return pathFound;
        }

        public bool FindChainFrom(NodeModel n, List<int> find, HashSet<int> traversed = null)
        {
            if (traversed == null)
                traversed = new HashSet<int>();

            if (traversed.Contains(n.ID))
                return false;

            traversed.Add(n.ID);

            bool pathFound = false;

            if (n.XNode.Independencies == null)
                return false;

            foreach (var d in n.XNode.Independencies)
            {
                var parent = Model.NodeModels[d];
                bool addLink = false;

                if (find.Contains(d))
                {
                    addLink = true;
                    find.Remove(d);
                }

                if (find.Count > 0 && FindChainFrom(parent, find, traversed))
                    addLink = true;

                if (addLink)
                {
                    PositionMap[parent.ID] = parent;

                    parent.AddIntermediateDependency(n);
                    pathFound = true;
                }
            }

            return pathFound;
        }
    }

    public class Graph
    {
        public Rank[] Ranks;
        public long Weight;

        public float ScaledHeight;
        public float ScaledOffset;


        public IEnumerable<NodeModel> Nodes()
        {
            foreach (var r in Ranks)
                foreach (var n in r.Column)
                    yield return n;
        }
    }

    public class Rank
    {
        internal List<NodeModel> Column = new List<NodeModel>();

        internal float MinHeightSpace;
    }
}
