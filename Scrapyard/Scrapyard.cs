/*
    KSP4X, written by Sami Boustani, aka Sirius Sam.
*/
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using KSP.IO;

namespace Scrapyard
{
    public enum Resource_Display { Summary, Detail }

    public class Catalog
    {
        public Dictionary<string, float> List = new Dictionary<string, float>();

        public void Add(string key, float qty = 1)
        {
            float total = 0;
            List[key] = List.TryGetValue(key, out total) ? total + qty : qty;
        }
        public void Remove(string key, float qty = 1)
        {
            Add(key, -qty);
        }
        public float Get(string key)
        {
            if (!String.IsNullOrEmpty(key))
            {
                float qty;
                return List.TryGetValue(key, out qty) ? qty : 0;
            }
            else return 0;
        }
        public void Set(string key, float qty)
        {
            List[key] = qty;
        }
    }

    public class Scrapyard
    {
        static Scrapyard instance = null;
        static Dictionary<string, string> resourceCodes = new Dictionary<string, string> {
                {"LiquidFuel","LF"},
                {"Oxidizer","Ox"},
                {"SolidFuel","SF"},
                {"MonoPropellant","MP"},
                {"XenonGas","Xe"}
        };

        public Catalog Parts = new Catalog();
        public Catalog Resources = new Catalog();
        public static Scrapyard Instance { get { if (instance == null) instance = new Scrapyard(); return instance; } }

        #region Settings
        public static string QtyFormat = "{2}", //{0} = Quantity on assembly line, {1} = qty in stock, {2} = available - instore, 
                        MissingQtyFormat = "({3}$) {2}", //{3} = cost of missing resource
                        TankMissingFormat = "{4} {2}\n", // {4} = resource code
                        TankFormat = "{4} {2}\n";
        public Boolean recoverResources = true;
        public static Resource_Display resourceDisplay = Resource_Display.Detail;
        #endregion

        #region I/O
        /// <summary>
        /// Called when the game is saved, commits inventory values into save game.
        /// </summary>
        /// <param name="root">ConfigNode passed from GameEvent</param>
        public void Commit(ConfigNode root)
        {
            // Parts
            ConfigNode N = new ConfigNode(this.GetType().FullName);
            ConfigNode p = N.AddNode("PARTS");
            foreach (string P in Parts.List.Keys.Where(x => Parts.List[x] != 0)) p.AddValue(P, Parts.List[P]);
            // Resources, load even if recover_resources = false
            ConfigNode r = N.AddNode("RESOURCES");
            foreach (string P in Resources.List.Keys.Where(x => Resources.Get(x) != 0)) r.AddValue(P, Resources.List[P]);

            root.AddNode(N);
        }
        /// <summary>
        /// Called when the game is loaded, loads inventory items into Parts list.
        /// </summary>
        /// <param name="root"></param>
        public void Rollback(ConfigNode root)
        {
            ConfigNode node = root.GetNode(this.GetType().FullName);
            if (node == null) throw new Exception("Inventory.Rollback() : instance not found, cancelling");
            else
            {
                // Load parts
                ConfigNode pnode = node.GetNode("PARTS");
                Parts.List.Clear();
                int qty = 0;
                foreach (ConfigNode.Value P in pnode.values)
                {
                    if (int.TryParse(P.value, out qty) && qty != 0) Parts.Set(P.name, qty);
                }

                // Load resources, even if recover_resources = false
                ConfigNode rnode = node.GetNode("RESOURCES");
                Resources.List.Clear();
                float Qty = 0;
                foreach (ConfigNode.Value P in rnode.values)
                {
                    if (float.TryParse(P.value, out Qty) && Qty != 0) Resources.Set(P.name, Qty);
                }
            }

            
        }
        #endregion


        #region Utility functions
        /// <summary>
        /// Stores a construct into the inventory, recursing on all subparts
        /// </summary>
        /// <param name="part">The root part from which recursion starts</param>
        public void Store(Part part)
        {
            Parts.Add(part.partInfo.name);
            foreach (Part P in part.children) Store(P);
        }
        /// <summary>
        /// Retrieves a construct from the inventory, recursing on all subparts
        /// </summary>
        /// <param name="part">The root part from which recursion starts</param>
        public void Retrieve(Part part)
        {
            Parts.Remove(part.partInfo.name);
            foreach (Part P in part.children) Retrieve(P);
        }

        /// <summary>
        /// Ship is rolled out, remove all parts present in the inventory and refund them
        /// </summary>
        /// <param name="construct"></param>
        public void  RolloutVessel(ShipConstruct construct) {
            String txt = "The following parts were in your inventory and were refunded :\n";
            float totalRefund = 0;           

            // Refund raw part price
            Dictionary<AvailablePart, int> parts = ListAllParts(construct.Parts);
            foreach (AvailablePart P in parts.Keys)
            {
                float rawprice = getPartRawPrice(P),
                    qty_on_ship = parts[P],
                    qty_in_store = (int)Parts.Get(P.name),
                    qty_used = Math.Min(qty_on_ship, qty_in_store);

                Parts.Set(P.name, Math.Max(0, qty_in_store - qty_on_ship));
                if (qty_used > 0)
                {
                    float Refund = qty_used * rawprice;
                    txt += String.Format("{0} * {1} = {2}$\n", (int)qty_used, P.title, (int)Refund);
                    totalRefund += Refund;
                }
            }

            // Refund resources if activated
            if (recoverResources)
            {
                Dictionary<string, double> resources = ListAllResources(construct.Parts);
                foreach (string R in resources.Keys)
                {
                    float price = getResourcePrice(R);
                    if (price != 0)
                    {
                        float qty_on_ship = (float)resources[R],
                            qty_in_store = Resources.Get(R),
                            qty_used = Math.Min(qty_on_ship, qty_in_store),
                            unitCost = getResourcePrice(R);

                        if (qty_used > 0)
                        {
                            txt += String.Format("{1} * {0} * {2}$ = {3}$\n", R, qty_on_ship, unitCost, qty_on_ship * unitCost);
                            Resources.Set(R, Math.Max(0, qty_in_store - qty_on_ship));
                            totalRefund += (float)qty_used * getResourcePrice(R);
                        }
                    }
                }
            }
            Funding.Instance.Funds += totalRefund;
            if (totalRefund > 0) PopupDialog.SpawnPopupDialog("You were refunded " + totalRefund + "$", txt, "ok!", false, HighLogic.Skin);
        }
        /// <summary>
        /// Ship has been recovered, store all parts and resources and cancel the stock refund.
        /// </summary>
        /// <param name="vessel"></param>
        public void RecoverVessel(ProtoVessel vessel)
        {
            foreach (ProtoPartSnapshot P in vessel.protoPartSnapshots)
            {
                // recover part
                Parts.Add(P.partInfo.name);
                Funding.Instance.Funds -= getPartRawPrice(P.partInfo);

                // recover part's resources
                if (recoverResources) foreach (ProtoPartResourceSnapshot res in P.resources)
                {
                    float amount = 0;
                    float.TryParse(res.resourceValues.GetValue("amount"), out amount);
                    float unitCost = getResourcePrice(res.resourceName);
                    if (unitCost != 0)
                    {
                        Resources.Add(res.resourceName, amount);
                        Funding.Instance.Funds -= unitCost * amount;
                    }
                }
            }
        }


        /// <summary>
        /// Recursively count all the parts in P
        /// </summary>
        /// <param name="dic">total is stored in this dictionnay</param>
        /// <param name="P">current part being scanned</param>
        public static Dictionary<AvailablePart, int> ListAllParts(Part P, Dictionary<AvailablePart, int> dic = null)
        {
            if (dic == null) dic = new Dictionary<AvailablePart, int>();
            AvailablePart id = P.partInfo;
            int qty = 1 + P.symmetryCounterparts.Count;
            if (dic.ContainsKey(id)) dic[id] += qty;
            else dic[id] = qty;


            PartResource r;
            foreach (Part C in P.children) ListAllParts(C, dic);
            return dic;
        }
        /// <summary>
        /// Count all the parts in a list of parts. This process is not recursive.
        /// </summary>
        /// <param name="list">list of parts to count</param>
        /// <returns>A dictionnary containing the Available parts and their quantity</returns>
        public static Dictionary<AvailablePart, int> ListAllParts(List<Part> list)
        {
            Dictionary<AvailablePart, int> result = new Dictionary<AvailablePart, int>();

            foreach (Part P in list)
            {
                AvailablePart id = P.partInfo;
                if (result.ContainsKey(id)) result[id] += 1;
                else result[id] = 1;
            }
            return result;
        }
        public static Dictionary<string, double> ListAllResources(List<Part> list)
        {
            Dictionary<string, double> result = new Dictionary<string, double>();
            foreach (Part P in list)
            {
                foreach (PartResource R in P.Resources)
                {
                    string rname = R.resourceName;
                    if (result.ContainsKey(rname)) result[rname] += R.amount;
                    else result[rname] = R.amount;
                    Debug.Log(P.name + "." + R.resourceName + " = " + R.amount);
                }
            }
            return result;
        }
        public static Boolean isExperimental(AvailablePart myPart)
        {
            return ResearchAndDevelopment.GetTechnologyState(myPart.TechRequired) != RDTech.State.Available;
        }
        public static Boolean hasResourceCode(string name)
        {
            return resourceCodes.ContainsKey(name);
        }
        public static Boolean isResource(string name)
        {
            return PartResourceLibrary.Instance != null && PartResourceLibrary.Instance.resourceDefinitions[name] != null;
        }
        public static string getResourceCode(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                string code = String.Empty;
                if (resourceCodes.TryGetValue(name, out code)) return code;
                else return name.Length > 2? name.Substring(0, 2): name;
            }
            else return "Xx";
        }
        public static float getResourcePrice(string R)
        {
            return isResource(R)? PartResourceLibrary.Instance.resourceDefinitions[R].unitCost:0;
        }
        public static float getPartRawPrice(AvailablePart P)
        {
            float price = P.cost;
            foreach (AvailablePart.ResourceInfo ri in P.resourceInfos)
            {
                int idx = ri.info.LastIndexOf("Cost:");
                if (idx != -1)
                {
                    string coststr = ri.info.Substring(idx + 5);
                    float c = 0;
                    if (float.TryParse(coststr, out c))
                    {
                        price -= c;
                    }
                }
            }
            return price;
        }
        #endregion
    }

    /// <summary>
    /// Runs only once, registers all game events.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EveryScene, true)]
    class Launcher : MonoBehaviour
    {

        public void Start()
        {
            // Persistence
            GameEvents.onGameStateLoad.Add(onGameStateLoad);
            GameEvents.onGameStateSave.Add(onGameStateSave);
            // Recovery/Construction
            GameEvents.onVesselRecovered.Add(OnVesselRecovered);
            GameEvents.OnVesselRollout.Add(OnVesselRollout);
        }

        #region I/O
        public void onGameStateLoad(ConfigNode root)
        {
            Scrapyard.Instance.Rollback(root);
        }
        public void onGameStateSave(ConfigNode root)
        {
            Scrapyard.Instance.Commit(root);
        }

        void OnVesselRollout(ShipConstruct construct)
        {
            Debug.Log("Rolling out vessel : Removing parts from inventory");
            Scrapyard.Instance.RolloutVessel(construct);
        }

        void OnVesselRecovered(ProtoVessel proto)
        {
            Debug.Log("Recovering ship " + proto.vesselName + ": Adding parts to Inventory");
            Scrapyard.Instance.RecoverVessel(proto);
        }
        #endregion


    }
}
