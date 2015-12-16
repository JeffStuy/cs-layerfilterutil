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
						createListOfFilters(lfCollect, tvLists);
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
						// search for the layer filter - is true if found - finalize the resBuf
						// and break

						LayerFilter lFilter = findAFilter(lfCollect, ((string)tvArgs[1].Value));

						if (lFilter != null)
						{
							tvLists.Add(convertFilterToList(lFilter));

							buildResBufMultipleItem(resbufOut, tvLists);
						}
						else
						{
							resbufOut = null;
						}
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
					// first parameter == "add"
					// second parameter == "filter name" (cannot be duplicate)
					// third parameter == "filter expression"
					// fourth (optional) parameter = "parent name" - ON HOLD

					// validate parameters
					// "add" already validated
					
					// minimum of 3 and maximum of 4 parameters

					bool argsGood = (tvArgs[1].TypeCode == (int)LispDataType.Text
						&& tvArgs[2].TypeCode == (int)LispDataType.Text);

					if ((tvArgs.Length == 3 && argsGood) ||
						(tvArgs.Length == 4 && argsGood 
							&& tvArgs[3].TypeCode == (int)LispDataType.Text)
						)
					{
						// here when correct number and type of args

						// first - make sure the proposed layer filter name
						// does not already exist

						if (findAFilter(lfCollect, (string)tvArgs[1].Value) == null)
						{
							// when null, an existing filter was not found - ok to proceed
							if (addOneFilter(lfTree, lfCollect, (string)tvArgs[1].Value, (string)tvArgs[2].Value))
							{
								createListOfFilters(lfCollect, tvLists);
							}
							else
							{
								resbufOut = null;
							}

						}
						else
						{
							// not null, existing filter found - cannot proceed
							resbufOut = null;
						}
					}
					else
					{
						// here when too many / few args 
						// or args of wrong type
						resbufOut = null;
					}



					break;
				case "delete":
					// deleting existing layer filter(s) - did only 2 args get
					// special case filter to delete = "*" means all of the existing filters
					// except those filters marked as "cannot delete"
					// provided and is the 2nd arg a text arg?  if yes, proceed
					if (tvArgs.Length == 2 && tvArgs[1].TypeCode == (int)LispDataType.Text)
					{
						// args validated - get here to delete a filter - 
						// this will return the original filter if it was found
						// and deleted and return null (nil) if not found
						LayerFilter lFilter = findAFilter(lfCollect, (string)tvArgs[1].Value);

						if (lFilter != null)
						{
							// when not null, filter found - OK to delete - if the
							// filter found is allowed to be deleted

							if (lFilter.AllowDelete)
							{
								// filter can be deleted

								// remove from local copy of the collection
								lfCollect.Remove(lFilter);

								// write the updated layer filter tree back to the database
								db.LayerFilters = lfTree;

								// update the layer palette to 
								// show the layer filter changes
								refreshLayerManager();

								// format the deleted filter into a list	
								tvLists.Add(convertFilterToList(lFilter));

								// build the result buffer
								buildResBufMultipleItem(resbufOut, tvLists);

							}
							else
							{
								// filter cannot be deleted
								resbufOut = null;
							}
						}
						else
						{
							resbufOut = null;
						}
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

		private void createListOfFilters(LayerFilterCollection lfCollect, List<List<TypedValue>> tvLists)
		{
			findFilters(lfCollect, tvLists);

			if (tvLists.Count != 0)
			{
				buildResBufMultipleItem(resbufOut, tvLists);
			}
			else
			{
				// just in case but this should never happen
				resbufOut = null;
			}
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


		private void buildResBufMultipleItem(ResultBuffer resBuffer, List<List<TypedValue>> tvLists)
		{

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

		private LayerFilter findAFilter(LayerFilterCollection lfC, string nameToFind)
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
						lFilterFound = findAFilter(lFilter.NestedFilters, nameToFind);
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
