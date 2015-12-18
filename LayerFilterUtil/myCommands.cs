// (C) Copyright 2015 by  
//
using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;

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
					// add a new layer filter to the layer filter collection
					// allow the filter to be added as a nested filter to another filter
					// except that any filter that cannot be deleted, cannot have nested filters
					// parameter options:
					// first (idx = 0) parameter == "add"
					// second (idx = 1) parameter == "filter type" either "property" or "group" (case does not matter)
					// third (idx = 2) parameter == "filter name" (cannot be duplicate)
					// fourth (idx = 3) parameter == "filter expression" for property filter
					// fourth (idx = 3) parameter == "layer ids" for a group filter
					// fifth (idx = 4) (optional) parameter = "parent name" - ON HOLD

					// possible add options:
					// add a property filter to the root of the collection
					// add a property filter to another layer filter (property or group)
					// add a group filter to the root of the collection
					// add a group filter to to another group filter (cannot be a property filter)


					// validate parameters
					// by getting to this point, first parameter validated

					// minimum of 4 and maximum of 5 parameters - all must be text

					// validate that the 2nd thru 4th arg are text
					if (tvArgs.Length != 4 && tvArgs.Length != 5) 
					{
						return null;
					}

					if (tvArgs[3].TypeCode != (int)LispDataType.Text
						&& tvArgs[3].TypeCode != (int)LispDataType.DottedPair)
					{
						return null;
					}
					
					if (tvArgs[1].TypeCode != (int)LispDataType.Text
						|| tvArgs[2].TypeCode != (int)LispDataType.Text
						|| (tvArgs.Length == 5
							&& tvArgs[4].TypeCode != (int)LispDataType.Text)
						) 
					{
						return null;
					}

					// at this point, we have the correct number of args and they are
					// all text args
					switch (((string)tvArgs[1].Value).ToLower()) 
					{
						case "property":
							// two cases - number of args = 4 or 5

							if (tvArgs.Length == 4)
							{
								// simple case - add a property filter
								ed.WriteMessage("Adding a property filter to the root of the tree");
								if (findOneFilter(lfCollect, (string)tvArgs[1].Value) == null)
								{
									// when null, an existing filter was not found - ok to proceed
									if (addOneFilter(lfTree, lfCollect, (string)tvArgs[1].Value, (string)tvArgs[2].Value))
									{
										resbufOut = listFilters(lfCollect, tvLists);
										break;
									}
									else
									{
										return null;
									}

								}
								else
								{
									// not null, existing filter found - cannot proceed
									return null;
								}
							} else {
								ed.WriteMessage("Adding a poperty filter to an existing filter");
								resbufOut = null;
							}
							break;
						case "group":
							// two cases - number or args = 4 or 5

							if (tvArgs.Length == 4) 
							{
								// simple case = add a group filter
								ed.WriteMessage("Adding a group filter to the root of the tree");
								resbufOut = null;
							}
							else
							{
								// add a filter to an existing filter - except that the
								// existing filter cannot be a property filter
								ed.WriteMessage("Adding a group filter to an existing filter");
								resbufOut = null;
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

		

		private ResultBuffer deleteFilter(LayerFilterCollection lfCollect, LayerFilterTree lfTree, string searchName)
		{

			LayerFilter lFilter = findOneFilter(lfCollect, searchName);

			if (lFilter == null || !lFilter.AllowDelete)
			{
				return null;
			} 

			// filter can be deleted

			// remove from local copy of the collection
			lfCollect.Remove(lFilter);

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

		private bool addOneFilter(LayerFilterTree lfTree, LayerFilterCollection lfCollect, string Name, string Expression)
		{
			// the layer filter collection is: lfCollect
			// since this may cause an error, use try catch
			try
			{
				// make a blank layer filter
				LayerFilter lF = new LayerFilter();

				// add the layer filter data
				lF.Name = Name;
				lF.FilterExpression = Expression;

				// add the filter to the collection
				lfCollect.Add(lF);

				// add the collection back to the date base
				db.LayerFilters = lfTree;

				// update the layer palette to show the 
				// layer filter changes
				refreshLayerManager();
			}
			catch (System.Exception ex)
			{
				// something did not work, return a null result buffer
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

			const int FILTERDELFLGDXF = 290;
			const int FILTERNESTFLGDXF = 291;
			const int FILTERGRPFLGDXF = 292;

			const int FILTERNESTCNTDXF = 90;

			List<TypedValue> tvList = new List<TypedValue>();

			makeDottedPair(tvList, FILTERNAMEDXF, lFilter.Name);
			makeDottedPair(tvList, FILTEREXPDXF, lFilter.FilterExpression);
			makeDottedPair(tvList, FILTERDELFLGDXF, lFilter.AllowDelete);
			makeDottedPair(tvList, FILTERPARENTDXF, 
				(lFilter.Parent != null ? lFilter.Parent.Name : ""));
			makeDottedPair(tvList, FILTERNESTFLGDXF, lFilter.AllowNested);
			makeDottedPair(tvList, FILTERNESTCNTDXF, lFilter.NestedFilters.Count);
			makeDottedPair(tvList, FILTERGRPFLGDXF, lFilter.IsIdFilter);

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

	}
}
