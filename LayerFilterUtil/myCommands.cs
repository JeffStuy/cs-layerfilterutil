// (C) Copyright 2015 by  
//
using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.LayerManager;

// This line is not mandatory, but improves loading performances
[assembly: CommandClass(typeof(LayerFilterUtil.MyCommands))]

namespace LayerFilterUtil
{

	// This class is instantiated by AutoCAD for each document when
	// a command is called by the user the first time in the context
	// of a given document. In other words, non static data in this class
	// is implicitly per-document!
	public class MyCommands
	{
		// message constants
		const string USAGEUSAGE = "(layerFilterUtil \"usage\")";
		const string USAGELIST = "(layerFilterUtil \"list\")";
		const string USAGEFIND = "(layerFilterUtil \"find\" FilterNameToFind)";
		const string USAGEADD = "(layerFilterUtil \"add\" NewFilterList)" + "this is a test";
		const string USAGEDEL = "(layerFilterUtil \"delete\" FilterNameToDelete or \"*\")";

		static int TabLevel = 0;
		static int NestDepth = 0;

		ResultBuffer resbufOut;

		Document doc;
		Database db;
		Editor ed;

		[LispFunction("LayerFilterUtil")]
		public ResultBuffer LayerFilterUtil(ResultBuffer args) // This method can have any name
		{
			doc = Application.DocumentManager.MdiActiveDocument;	// get reference to the current document
			db = doc.Database;										// get reference to the current dwg database
			ed = doc.Editor;											// get reference to the current editor (text window)

			resbufOut = new ResultBuffer();

			TypedValue[] tvArgs;

			List<List<TypedValue>> tvLists = new List<List<TypedValue>>();


			// reset for each usage
			NestDepth = 0;

			// process the args buffer
			// if passed is null, no good return null
			if (args != null)
			{
				// convert the args buffer into an array
				tvArgs = args.AsArray();

				// if the first argument is not text
				// return null
				if (tvArgs[0].TypeCode != (int)LispDataType.Text)
				{
					return null;
				}

				//displayArgs(tvArgs);
				//return null;

			}
			else
			{
				displayUsageMessage();
				return null; ;
			}


			// access to the collection of layer filters
			LayerFilterTree lfTree = db.LayerFilters;
			LayerFilterCollection lfCollect = lfTree.Root.NestedFilters;

			switch (((string)tvArgs[0].Value).ToLower())
			{
				case "list":
					// validate the args buffer, 2nd level - there can be only a single argument
					if (tvArgs.Length == 1)
					{

						resbufOut = listFilters(lfCollect, tvLists);
					} 
					else 
					{
						resbufOut = null;
					}

					break;
				case "find":
					// finding 1 existing layer filter - did only 2 args get
					// provided and is the 2nd arg a text arg?  if yes, proceed
					if (tvArgs.Length == 2 && tvArgs[1].TypeCode == (int)LispDataType.Text)
					{
						// search for the layer filter 

						resbufOut = findFilter(lfCollect, ((string)tvArgs[1].Value));

					}
					else
					{
						resbufOut = null;
					}
					break;
				case "add":
					const int FUNCTION = 0;
					const int F_NAME = 1;
					const int F_TYPE = 2;
					const int F_PARENT = 3;
					const int F_EXPRESSION = 4;
					const int F_LAYERS = 4;
					const int F_MIN = 5;



					// add a new layer filter to the layer filter collection
					// allow the filter to be added as a nested filter to another filter
					// except that any filter that cannot be deleted, cannot have nested filters
					// parameter options:
					// first	(idx = 0 FUNCTION) parameter == "add"
					// second	(idx = 1 F_NAME) parameter == "filter name" (cannot be duplicate)
					// third	(idx = 2 F_TYPE) parameter == "filter type" either "property" or "group" (case does not matter)
					// fifth	(idx = 3 F_PARENT) parameter == "parent name" or "" or nil for no parent name
					// fourth	(idx = 4 F_EXPRESSION) parameter == "filter expression" for property filter
					// fourth	(idx = 4 F_LAYERS) parameter == "layer ids" for a group filter
					

					// possible add options:
					// add a property filter to the root of the collection
					// add a property filter to another layer filter (property or group)
					// add a group filter to the root of the collection
					// add a group filter to to another group filter (cannot be a property filter)


					// validate parameters
					// by getting to this point, first parameter validated

					//ed.WriteMessage("\n@0");
					//displayArgs(tvArgs);


					// minimum of 5 parameters
					if (tvArgs.Length < F_MIN) 
					{
						return null;
					}
					
					// parameters 1 & 2 must be text
					// parameter 3 must be text or nil
					// or new filter cannot already exist
					if (tvArgs[F_NAME].TypeCode != (int)LispDataType.Text
						|| tvArgs[F_TYPE].TypeCode != (int)LispDataType.Text
						|| (tvArgs[F_PARENT].TypeCode != (int)LispDataType.Text
						&& tvArgs[F_PARENT].TypeCode != (int)LispDataType.Nil)
						|| findOneFilter(lfCollect, (string)tvArgs[F_NAME].Value) != null)
					{
						return null;
					}
					
					// parameter 4+ must be text
					for (int i = F_EXPRESSION; i < tvArgs.Length; i++)
					{
						if (tvArgs[i].TypeCode != (int)LispDataType.Text)
							return null;
					}

					// at this point, we have the correct type of args (text or nil)
					switch (((string)tvArgs[F_TYPE].Value).ToLower())
					{
						case "property":
							
							// two cases - add to root of tree or add to existing
							// if tvArgs[F_PARENT] == "" or tvArgs[F_PARENT] == nil, add to tree root
							// if tvArgs[F_PARENT] == string, add to existing parent

							if (tvArgs[F_PARENT].TypeCode == (int)LispDataType.Nil || ((string)tvArgs[F_PARENT].Value).Length == 0)
							{
								// already checked that new filter does not exist - ok to proceed
								if (addOneFilter(lfTree, lfCollect, 
									(string)tvArgs[F_NAME].Value, (string)tvArgs[F_EXPRESSION].Value, null))
								{
									return findFilter(lfCollect, (string)tvArgs[F_NAME].Value);
								}

							}
							else
							{
								// bit more complex - add a layer filter to an existing layer filter (nested layer filter)
								LayerFilter lfParent = findOneFilter(lfCollect, (string)tvArgs[F_PARENT].Value);

								if (lfParent != null) 
								{
									// already checked that the new filter does not exist - ok to proceed
									if (addOneFilter(lfTree, lfCollect, 
										(string)tvArgs[F_NAME].Value, 
											(string)tvArgs[F_EXPRESSION].Value, lfParent))
									{
										return findFilter(lfCollect, (string)tvArgs[F_NAME].Value);
									}
								}
							}

							// get here, something did not work - return nill
							return null;
							break;
						case "group":

							// two cases - have or have not parent

							if (tvArgs[F_PARENT].TypeCode == (int)LispDataType.Nil || ((string)tvArgs[F_PARENT].Value).Length == 0)
							{
								// simple case - add group filter to the tree root

								// args at this point:
								// FUNCTION = "add" - already verified
								// F_NAME = filter name - already verified
								// F_TYPE = "group" - already verified
								// F_PARENT = filter parent is blank or nil - already verified
								// F_LAYERS = begining of the list of layers to include in the group filter
								ObjectIdCollection layIds = new ObjectIdCollection();
								SortedList<string, int> layerNames = new SortedList<string, int>();


								// store the list of layers to add into a sorted list
								for (int i = F_LAYERS; i < tvArgs.Length; i++)
								{
									layerNames.Add(((string)tvArgs[i].Value).ToLower(), 1);
								}

								// process the list of layers and get their layer ids
								layIds = getSelectedLayerIds(layerNames);


								// now have a list of layer id's for the layer group
								// now add the layer filter group and its layer id's

								// create a blank layer filter group
								LayerGroup lg = new LayerGroup();

								// set its name
								lg.Name = (string)tvArgs[F_NAME].Value;

								// add each layer id for the group
								foreach (ObjectId layId in layIds)
								{
									lg.LayerIds.Add(layId);
								}

								// add the layer filter group to the collection
								lfCollect.Add(lg);

								// update the database with the updated tree
								db.LayerFilters = lfTree;

								// provide the return information
								return findFilter(lfCollect, (string)tvArgs[F_NAME].Value);
							}
							else
							{
								// complex case - add group filter to an existing filter
								// existing filter cannot be a property filter

								// args at this point:
								// FUNCTION = "add" - already verified
								// F_NAME = filter name - already verified
								// F_TYPE = "group" - already verified
								// F_PARENT = filter parent is not blank - already verified
								// F_LAYERS = begining of the list of layers to include in the group filter

								ed.WriteMessage("Adding a group filter to an existing filter: " + (string)tvArgs[F_PARENT].Value + "\n");

								LayerFilter lfParent = findOneFilter(lfCollect, (string)tvArgs[F_PARENT].Value);

								if (lfParent != null)
								{
									return null;
								}

								// get here - something did not work - return nil
								return null;
							}

							break;
						default:
							
							return null;
					}

					break;
				case "delete":
					// deleting existing layer filter(s) - did only 2 args get
					// special case filter to delete = "*" means all of the existing filters
					// except those filters marked as "cannot delete"
					// provided and is the 2nd arg a text arg?  if yes, proceed
					if (tvArgs.Length == 2 && tvArgs[1].TypeCode == (int)LispDataType.Text)
					{

						resbufOut = deleteFilter(lfCollect, lfTree, (string)tvArgs[1].Value);

					}
					else
					{
						resbufOut = null;
					}
					break;
				case "usage":
					displayUsageMessage();
					resbufOut = null;
					break;
				default:
					resbufOut = null;
					break;
			}

			return resbufOut;
		}

		private ObjectIdCollection getSelectedLayerIds(SortedList<string, int> layerNames)
		{
			ObjectIdCollection layIds = new ObjectIdCollection();

			// process the list of layers and place them into a sorted list
			// since working with a database item, use a transaction
			Transaction tr = db.TransactionManager.StartTransaction();

			using (tr)
			{
				LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

				foreach (ObjectId layId in lt)
				{
					LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(layId, OpenMode.ForRead);

					// check the name of the layer against the list of names to
					// add - if the key is contained, save the layer id and remove the
					// key from the add list
					if (layerNames.ContainsKey(ltr.Name.ToLower()))
					{
						layIds.Add(layId);
						layerNames.Remove(ltr.Name.ToLower());

						// if all of the layers get removed, done
						if (layerNames.Count == 0)
						{
							break;
						}
					}
				}

				tr.Commit();
			}

			return layIds;
		}

		

		private ResultBuffer deleteFilter(LayerFilterCollection lfCollect, LayerFilterTree lfTree, string searchName)
		{

			LayerFilter lFilter = findOneFilter(lfCollect, searchName);

			if (lFilter == null || !lFilter.AllowDelete)
			{
				return null;
			} 

			// filter can be deleted

			// remove from local copy of the collection
			if (lFilter.Parent == null)
			{
				// remove from the root collection when
				// parent is null
				lfCollect.Remove(lFilter);
			}
			else
			{
				// else remove from the parent collection
				// when parent is not null
				lFilter.Parent.NestedFilters.Remove(lFilter);
			}

			// write the updated layer filter tree back to the database
			db.LayerFilters = lfTree;

			// update the layer palette to 
			// show the layer filter changes
			refreshLayerManager();

			List<List<TypedValue>> tvLists = new List<List<TypedValue>>();

			// format the deleted filter into a list	
			tvLists.Add(convertFilterToList(lFilter));

			// build & return the result buffer
			return buildResBufMultipleItem(tvLists);
		}



		/// <summary>
		/// Finds a single filter from the Layer Filter Collection<para />
		/// Returns a Result Buffer with the Layer Filter information
		/// </summary>
		/// <param name="lfCollect"></param>
		/// <param name="searchName"></param>
		/// <returns></returns>
		private ResultBuffer findFilter(LayerFilterCollection lfCollect, string searchName)
		{
			LayerFilter lFilter = findOneFilter(lfCollect, searchName);

			if (lFilter == null)
			{
				return null;
			}

			List<List<TypedValue>> tvLists = new List<List<TypedValue>>();
	
			tvLists.Add(convertFilterToList(lFilter));

			return buildResBufMultipleItem(tvLists);
		}




		// update the layer palette to show the 
		// layer filter changes
		private void refreshLayerManager()
		{
			object manager = Application.GetSystemVariable("LAYERMANAGERSTATE");

			// force a refresh of the layermanager palette to have
			// the changes show up
			if (manager.ToString().Contains("1"))
			{
				doc.SendStringToExecute("layerpalette ", true, false, false);
			}

		}


		// add one layer filter to an existing parent layer filter (nested filter)
		private bool addOneFilter(LayerFilterTree lfTree, LayerFilterCollection lfCollect, 
			string Name, string Expression, LayerFilter Parent) 
		{

			if (Parent != null && Parent.AllowNested != true)
			{
				return false;
			}

			// the layer filter collection is lfCollect
			// since this may cause an error, use try catch
			try
			{
				// make an empty layer filter
				LayerFilter lf = new LayerFilter();

				// add the layer filter data
				lf.Name = Name;
				lf.FilterExpression = Expression;


				if (Parent == null)
				{
					// add the filter to the collection
					lfCollect.Add(lf);
				} else 
				{
					// add the layer filter as a nested filter
					Parent.NestedFilters.Add(lf);
				}

				// add the collection back to the data base
				db.LayerFilters = lfTree;

				// update the layer palette to show the
				// layer filter changes
				refreshLayerManager();
			}
			catch (System.Exception ex)
			{
				// something did not work, return false
				return false;
			}

			return true;
		}

		private ResultBuffer listFilters(LayerFilterCollection lfCollect, List<List<TypedValue>> tvLists)
		{

			findFilters(lfCollect, tvLists);

			if (tvLists.Count == 0)
			{
				// just in case but this should never happen
				return null;
			}

			return buildResBufMultipleItem(tvLists);;
		}

		private void displayUsageMessage()
		{
			ed.WriteMessage("Usage:\n" + USAGEUSAGE +
				" or\n" + USAGELIST + " or\n" + USAGEFIND +
				" or\n" + USAGEADD + " or\n" + USAGEDEL + "\n");
		}

		private void buildErrorMessage(ResultBuffer resBuffer, string Message)
		{
			resBuffer.Add(
				new TypedValue((int)LispDataType.ListBegin));
			resBuffer.Add(
				new TypedValue((int)LispDataType.Int16, (short)-1));
			resBuffer.Add(
				new TypedValue((int)LispDataType.Text, "Invalid - Usage: " + Message));
			resBuffer.Add(
				new TypedValue((int)LispDataType.ListEnd));
		}


		private ResultBuffer buildResBufMultipleItem(List<List<TypedValue>> tvLists)
		{
			ResultBuffer resBuffer = new ResultBuffer();

			resBuffer.Add(new TypedValue((int)LispDataType.ListBegin));

			// add a dotted pari that represents the item count
			resBuffer.Add(
				new TypedValue((int)LispDataType.Int16, tvLists.Count));

			// begin the list of lists
			resBuffer.Add(new TypedValue((int)LispDataType.ListBegin));

			foreach (List<TypedValue> tvList in tvLists)
			{
				buildResBufSingleItem(resBuffer, tvList);
			}


			// end the list of lists
			resBuffer.Add(new TypedValue((int)LispDataType.ListEnd));

			// end the whole list
			resBuffer.Add(new TypedValue((int)LispDataType.ListEnd));

			return resBuffer;
		}

		private void buildResBufSingleItem(ResultBuffer resBuffer, List<TypedValue> tvList)
		{
			// begin adding a new inner list
			resBuffer.Add(new TypedValue((int)LispDataType.ListBegin));

			foreach (TypedValue tVal in tvList)
			{
				resBuffer.Add(tVal);
			}

			// end the inner list
			resBuffer.Add(new TypedValue((int)LispDataType.ListEnd));
		}

		/**
		 * Find all layer filters and adds each layer filter to the List
		 */ 
		private void findFilters(LayerFilterCollection lfC, List<List<TypedValue>> tvLists)
		{
			// if nothing in the collection, return null
			if (lfC.Count == 0 || NestDepth > 100)
			{
				return;
			}

			NestDepth++;

			foreach (LayerFilter lFilter in lfC)
			{

				tvLists.Add(convertFilterToList(lFilter));

				if (lFilter.NestedFilters.Count != 0)
				{
					findFilters(lFilter.NestedFilters, tvLists);
				}
			}
			NestDepth--;
		}


		/// <summary>
		/// Scan through the the layer filter collection and find a filter<para />
		/// that matches the name provided - exact match is required
		/// </summary>
		/// <param name="lfC">A Layer Filter Collection</param>
		/// <param name="nameToFind">The name of the Layer Filter to find</param>
		/// <returns></returns>
		private LayerFilter findOneFilter(LayerFilterCollection lfC, string nameToFind)
		{
			LayerFilter lFilterFound = null;
			

			if (lfC.Count == 0 || NestDepth > 100)
			{
				return null;
			}

			NestDepth++;

			foreach (LayerFilter lFilter in lfC)
			{

				//ed.WriteMessage("\nchecking: " + lFilter.Name + " vs. to find: " + nameToFind);

				if (lFilter.Name == nameToFind)
				{
					lFilterFound = lFilter;
					break;
				}
				else
				{
					if (lFilter.NestedFilters.Count != 0)
					{
						lFilterFound = findOneFilter(lFilter.NestedFilters, nameToFind);
						if (lFilterFound != null)
						{
							break;
						}
					}
				}
			}

			NestDepth--;

			return lFilterFound;
		}

		private List<TypedValue> convertFilterToList(LayerFilter lFilter)
		{
			const int FILTERNAMEDXF = 300;
			const int FILTEREXPDXF = 301;
			const int FILTERPARENTDXF = 302;
			const int FILTERLAYERSDXF = 303;

			const int FILTERDELFLGDXF = 290;
			const int FILTERNESTFLGDXF = 291;
			const int FILTERGRPFLGDXF = 292;

			const int FILTERNESTCNTDXF = 90;

			List<TypedValue> tvList = new List<TypedValue>();

			makeDottedPair(tvList, FILTERNAMEDXF, lFilter.Name);

			if (!lFilter.IsIdFilter)
			{ 
				makeDottedPair(tvList, FILTEREXPDXF, lFilter.FilterExpression);
			}
			else
			{
				StringBuilder sb = new StringBuilder();
				using (Transaction tr = db.TransactionManager.StartTransaction())
				{
					LayerTableRecord layRecord;


					foreach (ObjectId layId in ((LayerGroup)lFilter).LayerIds)
					{

						layRecord = tr.GetObject(layId, OpenMode.ForRead) as LayerTableRecord;

						sb.Append("\"" + layRecord.Name + "\" ");
					}
				}

				if (sb.Length == 0) { sb = new StringBuilder("\"\"", 1); }

				makeDottedPair(tvList, FILTERLAYERSDXF, sb.ToString());
			}

			makeDottedPair(tvList, FILTERDELFLGDXF, lFilter.AllowDelete);
			makeDottedPair(tvList, FILTERPARENTDXF, 
				(lFilter.Parent != null ? lFilter.Parent.Name : ""));
			makeDottedPair(tvList, FILTERGRPFLGDXF, lFilter.IsIdFilter);
			makeDottedPair(tvList, FILTERNESTFLGDXF, lFilter.AllowNested);
			makeDottedPair(tvList, FILTERNESTCNTDXF, lFilter.NestedFilters.Count);


			return tvList;
		}

		private void makeDottedPair(List<TypedValue> tvList, int dxfCode, bool Value)
		{
			makeDottedPair(tvList, dxfCode, (short)(Value ? 1 : 0));
		}


		private void  makeDottedPair<T>(List<TypedValue> tvList, int dxfCode, T Value)
		{

			tvList.Add(new TypedValue((int)LispDataType.ListBegin));
			tvList.Add(new TypedValue((int)LispDataType.Int16, dxfCode));

			switch (Type.GetTypeCode(typeof(T)))
			{
				case TypeCode.String:
					tvList.Add(
						new TypedValue((int)LispDataType.Text, Value));
					break;
				case TypeCode.Int16:
					tvList.Add(
						new TypedValue((int)LispDataType.Int16, Value));
					break;
				case TypeCode.Int32:
					tvList.Add(
						new TypedValue((int)LispDataType.Int32, Value));
					break;
				case TypeCode.Empty:
					tvList.Add(
						new TypedValue((int)LispDataType.Text, ""));
					break;
			}
			tvList.Add(new TypedValue((int)LispDataType.DottedPair));
		}


		private string describeLispDateType(short tv)
		{
		// todo - complete the below list
			switch (tv)
			{
				case (short)LispDataType.DottedPair:
					return "Dotted pair";
					break;
				case (short)LispDataType.Int16:
					return "Int16";
					break;
				case (short)LispDataType.Int32:
					return "Int32";
					break;
				case (short)LispDataType.ListBegin:
					return "ListBegin";
					break;
				case (short)LispDataType.ListEnd:
					return "ListEnd";
					break;
				case (short)LispDataType.None:
					return "None";
					break;
				case (short)LispDataType.Nil:
					return "Nil";
					break;
				case (short)LispDataType.Void:
					return "Dotted pair";
					break;
				case (short)LispDataType.Text:
					return "Text";
					break;
				default:
					return tv.ToString();

			}
		}

		private void displayArgs(TypedValue[] tvArgs)
		{
			for (int i = 0; i < tvArgs.Length; i++)
			{
				ed.WriteMessage("arg#: " + i
					+ " : type: " + " \"" + describeLispDateType(tvArgs[i].TypeCode) + "\" (" + tvArgs[i].TypeCode + ")"
					+ " : value: >" + tvArgs[i].Value + "<");

				if (tvArgs[i].TypeCode == (short)LispDataType.Text)
				{
					ed.WriteMessage(" : length: " + ((string)tvArgs[i].Value).Length);
				}

				ed.WriteMessage("\n");

			}
		}

	}
}
