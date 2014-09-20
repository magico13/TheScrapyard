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
        private Dictionary<string, float> List = new Dictionary<string, float>();
        public Dictionary<string, float> Inventory { get { return List; } } //You can read the inventory but can't write to it. May need to make a copy of the List, but not positive.
        //ALL interactions with the List must go through "official channels" for API purposes

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
        public void Clear()
        {
            List.Clear();
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
            foreach (string P in Parts.Inventory.Keys.Where(x => Parts.Inventory[x] != 0)) p.AddValue(P, Parts.Inventory[P]);
            // Resources, load even if recover_resources = false
            ConfigNode r = N.AddNode("RESOURCES");
            foreach (string P in Resources.Inventory.Keys.Where(x => Resources.Get(x) != 0)) r.AddValue(P, Resources.Inventory[P]);

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
                Parts.Clear();
                int qty = 0;
                foreach (ConfigNode.Value P in pnode.values)
                {
                    if (int.TryParse(P.value, out qty) && qty != 0) Parts.Set(P.name, qty);
                }

                // Load resources, even if recover_resources = false
                ConfigNode rnode = node.GetNode("RESOURCES");
                Resources.Clear();
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
        /// Ship is rolled out, remove all parts present in the inventory and refund them
        /// </summary>
        /// <param name="construct"></param>
        public void  RolloutVessel(ShipConstruct construct) {
            float totalRefund = 0;           

            // Refund raw part price. Now supports TweakScale and other mods that alter prices after load
            foreach (Part P in construct.Parts)
            {
                string partName = NameWithTS(P.protoPartSnapshot);
                float rawPrice = getPartRawPrice(P);
                int qty_in_store = (int)Parts.Get(partName);
                if (qty_in_store > 0)
                {
                    Parts.Remove(partName);
                    totalRefund += rawPrice;
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
                            Resources.Set(R, Math.Max(0, qty_in_store - qty_on_ship));
                            totalRefund += (float)qty_used * getResourcePrice(R);
                        }
                    }
                }
            }
            Funding.Instance.Funds += totalRefund;
        }
        /// <summary>
        /// Ship has been recovered, store all parts and resources and cancel the stock refund.
        /// </summary>
        /// <param name="vessel"></param>
        public void RecoverVessel(ProtoVessel vessel)
        {
            //Need to know the distance to cancel out the refund correctly
            double distanceFromKSC = SpaceCenter.Instance.GreatCircleDistance(SpaceCenter.Instance.cb.GetRelSurfaceNVector(vessel.latitude, vessel.longitude));
            double maxDist = SpaceCenter.Instance.cb.Radius * Math.PI;
            float recoveryPercent = Mathf.Lerp(0.98f, 0.1f, (float)(distanceFromKSC / maxDist));

            if (vessel.landedAt == "LaunchPad" || vessel.landedAt == "Runway") //TODO: Double check these strings
                recoveryPercent = 1f;

            foreach (ProtoPartSnapshot P in vessel.protoPartSnapshots)
            {
                // recover part
                string fullName = NameWithTS(P);
                Parts.Add(fullName);
                Funding.Instance.Funds -= (getPartRawPrice(P) * recoveryPercent);

                // recover part's resources
                if (recoverResources) foreach (ProtoPartResourceSnapshot res in P.resources)
                {
                    float amount = 0;
                    float.TryParse(res.resourceValues.GetValue("amount"), out amount);
                    float unitCost = getResourcePrice(res.resourceName);
                    if (unitCost != 0)
                    {
                        Resources.Add(res.resourceName, amount);
                        Funding.Instance.Funds -= (unitCost * amount * recoveryPercent);
                    }
                }
            }
        }

        public Dictionary<string, double> ListAllResources(List<Part> list)
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
        public float TotalVesselCostAfterInventory(List<Part> VesselParts)
        {
            float costRefunded = 0, totalCost = 0;
            Dictionary<string, float> InventoryCopy = new Dictionary<string,float>(Parts.Inventory);
            foreach (Part P in VesselParts)
            {
                string partName = NameWithTS(P.protoPartSnapshot);
                float rawPrice = getPartRawPrice(P);
                int qty_in_store = (int)  (InventoryCopy.ContainsKey(partName) ? InventoryCopy[partName] : 0);
                if (qty_in_store > 0)
                {
                    InventoryCopy.Remove(partName);
                    costRefunded += rawPrice;
                }
                totalCost += rawPrice;
            }

            Dictionary<string, float> ResourceCopy = new Dictionary<string, float>(Resources.Inventory);
            if (recoverResources)
            {
                Dictionary<string, double> resources = ListAllResources(VesselParts);
                foreach (string R in resources.Keys)
                {
                    float price = getResourcePrice(R);
                    if (price != 0)
                    {
                        float qty_on_ship = (float)resources[R],
                            qty_in_store = (ResourceCopy.ContainsKey(R) ? ResourceCopy[R] : 0),
                            qty_used = Math.Min(qty_on_ship, qty_in_store);

                        if (qty_used > 0)
                        {
                            ResourceCopy[R] = (qty_in_store - qty_used);
                            costRefunded += (float)qty_used * price;
                        }
                        totalCost += (qty_used * price);
                    }
                }
            }
            return (totalCost - costRefunded);
        }
        public Dictionary<string, int> PartListToDict(List<Part> list)
        {
            Dictionary<string, int> dic = new Dictionary<string,int>();
            foreach (Part P in list)
            {
                string name = NameWithTS(P.protoPartSnapshot);
                if (dic.ContainsKey(name))
                    ++dic[name];
                else
                    dic.Add(name, 1);
            }
            return dic;
        }
        public Boolean isExperimental(AvailablePart myPart)
        {
            return ResearchAndDevelopment.GetTechnologyState(myPart.TechRequired) != RDTech.State.Available;
        }
        public Boolean hasResourceCode(string name)
        {
            return resourceCodes.ContainsKey(name);
        }
        public Boolean isResource(string name)
        {
            return PartResourceLibrary.Instance != null && PartResourceLibrary.Instance.resourceDefinitions[name] != null;
        }
        public string getResourceCode(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                string code = String.Empty;
                if (resourceCodes.TryGetValue(name, out code)) return code;
                else return name.Length > 2? name.Substring(0, 2): name;
            }
            else return "Xx";
        }
        public float getResourcePrice(string R)
        {
            return isResource(R)? PartResourceLibrary.Instance.resourceDefinitions[R].unitCost:0;
        }
        public float getPartRawPrice(Part P)
        {
            return getPartRawPrice(P.protoPartSnapshot);
        }
        public float getPartRawPrice(ProtoPartSnapshot P)
        {
            float dryCost, fuelCost;
            ShipConstruction.GetPartCosts(P, P.partInfo, out dryCost, out fuelCost);
            return dryCost;
        }
        public AvailablePart GetAvailablePartByName(string partName)
        {
            return PartLoader.LoadedPartsList.FirstOrDefault(p => p.name == StripTweakScaleInfo(partName));
        }

        //The following is all tweakscale info
        public string GetTweakScaleSize(ConfigNode partNode)
        {
            string partSize = "";
            if (partNode.HasNode("MODULE"))
            {
                ConfigNode[] Modules = partNode.GetNodes("MODULE");
                if (Modules.Length > 0 && Modules.FirstOrDefault(mod => mod.GetValue("name") == "TweakScale") != null)
                {
                    ConfigNode tsCN = Modules.First(mod => mod.GetValue("name") == "TweakScale");
                    string defaultScale = tsCN.GetValue("defaultScale");
                    string currentScale = tsCN.GetValue("currentScale");
                    if (!defaultScale.Equals(currentScale))
                        partSize = currentScale;
                }
            }
            return partSize;
        }
        public string GetTweakScaleSize(ProtoPartSnapshot partSnapshot)
        {
            string partSize = "";
            ProtoPartModuleSnapshot tweakscale = partSnapshot.modules.Find(mod => mod.moduleName == "TweakScale");
            if (tweakscale != null)
            {
                ConfigNode tsCN = tweakscale.moduleValues;
                string defaultScale = tsCN.GetValue("defaultScale");
                string currentScale = tsCN.GetValue("currentScale");
                if (!defaultScale.Equals(currentScale))
                    partSize = currentScale;
            }
            return partSize;
        }
        public string StripTweakScaleInfo(string partName)
        {
            return partName.Split(',')[0];
        }
        public string NameWithTS(ProtoPartSnapshot PPS)
        {
            string tweakscaleSize = GetTweakScaleSize(PPS);
            return PPS.partInfo.name + (tweakscaleSize != "" ? "," + tweakscaleSize : "");
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
