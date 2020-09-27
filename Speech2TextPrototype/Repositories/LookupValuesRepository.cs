﻿using Microsoft.EntityFrameworkCore;
using Speech2TextPrototype.Data;
using Speech2TextPrototype.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Speech2TextPrototype.Repositories
{
    public class LookupValuesRepository : ILookupValuesRepository
    {
        private readonly florisContext _context;

        /// <summary>List of selected measures, e.g. M_SALES_VALUE</summary>
        private List<string> listMeasures = new List<string>();

        /// <summary>Value of the selected measure in the lookup table, e.g. sale item </summary>
        private List<string> listMeasureValues = new List<string>();

        /// <summary>List of sql date filters, e.g. convert(varchar(7), PERIOD_START, 23) = '2016-01'</summary>
        private List<string> listDates = new List<string>();

        /// <summary>Value of date filters, e.g. january 2016</summary>
        private List<string> listDateValues = new List<string>();

        /// <summary>List of database-related filters e.g. Brand: "Agros"</summary>
        private List<string> listWhereStatements = new List<string>();

        /// <summary>Array of statically defined aggregate functions "max, sum"</summary>
        string[] aggregates = { "max", "min", "sum", "count", "avg" };

        /// <summary>Array of statically defined aggregate functions closer to natural language e.g. "maximum, total"</summary>
        string[] aggAlternatives = { "maximum", "minimum", "total", "number", "average" };

        /// <summary>String of the aggregate function that is mapped between the 2 arrays (aggregates, aggAlternatives), e.g. Maximum => max</summary>
        private string aggregateFunction = "";

        /// <summary>Indicates whether there is a "between" keyword in the query</summary>
        private bool isBetween = false;

        // Used for Past x Months, Days
        /// <summary>String that indicates the number of Past Years or Months or Days</summary>
        private string timeNum = "1";

        /// <summary>String which is used for Past X {Year, Month, Day}</summary>
        private string timeType = "";

        public LookupValuesRepository(florisContext context)
        {
            _context = context;
        }

        /// <summary>
        /// MAIN FUNCTION
        /// Query the database based on the natural language tokens received from the Python NLTK api
        /// </summary>
        /// <param name="res">Python Tokenizer Response</param>
        /// <returns>Tuple containing: the table response, the list of measurables, the list of dates and the query as a string</returns>
        public LookupOutputModel token2Sql(PyRes res)
        {
            string[] tokens = res.tokens;
            string[] bigrams = res.bigrams;

            bigramLookup(bigrams);

            tokens = DeleteKnownTokens(tokens);

            tokenLookup(tokens);

            string query = ConstructQuery();

            double scalar = -1;

            var result = _context.tdata.FromSqlRaw(query);

            // Check for scalar values (Max, Sum, etc.)
            // Uses reflection for the measurable to avoid numerous if statements (x3 code)
            if (!String.IsNullOrEmpty(aggregateFunction) && result.Any())
            {
                Type type = typeof(TData);

                switch (aggregateFunction)
                {
                    case "max":
                        scalar = Convert.ToDouble(result.ToList().Max(r => type.GetProperty(listMeasures[0]).GetValue(r)));
                        break;
                    case "min":
                        scalar = Convert.ToDouble(result.ToList().Min(r => type.GetProperty(listMeasures[0]).GetValue(r)));
                        break;
                    case "sum":
                        scalar = result.ToList().Sum(r => Convert.ToDouble(type.GetProperty(listMeasures[0]).GetValue(r)));
                        break;
                    case "avg":
                        scalar = result.ToList().Average(r => Convert.ToDouble(type.GetProperty(listMeasures[0]).GetValue(r)));
                        break;
                }

                return new LookupOutputModel { data = new List<DisplayTable>(), measures = listMeasures, dates = listDates, querySql = query, scalarValue = scalar };
            }

            var dt = result.Select(r => new DisplayTable()
            {
                BRAND = r.BRAND,
                CATEGORY_NAME = r.CATEGORY_NAME,
                PERIOD_START = r.PERIOD_START,
                QUANTITY = r.QUANTITY,
                PRICE = r.PRICE,
                SIZE = r.SIZE
            }).ToList();

            // Insert results into displayTable to allow for serverside pagination,filtering etc
            _context.Database.ExecuteSqlRaw("TRUNCATE TABLE [DISPLAY_TABLE]");
            _context.displayTable.AddRange(dt);
            _context.SaveChanges();
            return new LookupOutputModel { data = dt, measures = listMeasures, dates = listDates, querySql = query, scalarValue = scalar };
        }
    

        /// <summary>
        /// Search for 2-word tokens as measures or dates
        /// </summary>
        /// <param name="bigrams">Array of 2-word tokens from the python script</param>
        /// <returns>void - passing by erence</returns>
        private void bigramLookup(string[] bigrams)
        {
            foreach (string bigram in bigrams)
            {
                LookupValues bigramLookupValues = _context.lookupvalues.Where(row => row.Value == bigram).FirstOrDefault();
                if (bigramLookupValues != null)
                {
                    // Find Measurables
                    if (bigramLookupValues.Type == "Measure")
                    {
                        listMeasures.Add(bigramLookupValues.WhereStmt);
                        listMeasureValues.Add(bigramLookupValues.Value);
                    }
                    // Find WhereStmts
                    else if (bigramLookupValues.Type == "Date")
                    {
                        listDates.Add(bigramLookupValues.WhereStmt);
                        listDateValues.Add(bigramLookupValues.Value);
                    }
                }
            }
        }


        /// <summary>
        /// Delete Tokens identified as known bigrams
        /// </summary>
        /// <param name="tokens">Array of single-word tokens from the python script</param>
        /// <returns>Updated tokens array</returns>
        private string[] DeleteKnownTokens(string[] tokens)
        {
            var listTokens = new List<string>(tokens);
            bool tokenDeleted = false;

            if (listMeasures.Any() || listDates.Any())
            {
                for (int i = listTokens.Count - 1; i >= 0; i--)
                {
                    // Delete measures - tokens
                    foreach (string measure in listMeasureValues)
                    {
                        if (measure.Contains(listTokens[i], StringComparison.OrdinalIgnoreCase))
                        {
                            listTokens.RemoveAt(i);
                            tokenDeleted = true;
                            break;
                        }
                    }


                    // Delete dates - tokens
                    if (!tokenDeleted) // Hotfix: Crashed because listTokens[i] was already deleted
                    {
                        foreach (string date in listDateValues)
                        {
                            if (date.Contains(listTokens[i], StringComparison.OrdinalIgnoreCase))
                            {
                                listTokens.RemoveAt(i);
                                break;
                            }
                        }
                    }
                    tokenDeleted = false;
                }
                tokens = listTokens.ToArray();
            }
            return tokens;
        }


        /// <summary>
        /// Search for single-word tokens as measures, aggregates, date or any other db-related filter
        /// </summary>
        /// <param name="tokens">Array of single-word tokens from the python script</param>
        /// <returns>void - passing by erence</returns>
        private void tokenLookup(string[] tokens)
        {
            int tokenIndex = 0;

            // Lookup Tokens
            foreach (string token in tokens)
            {
                // Find Between Keyword
                if (token == "between")
                {
                    isBetween = true;
                    continue;
                }

                if (token == "last" || token == "past")
                {
                    // Only Accept 2-digit numbers so that it will not produce an error when query does not contain timeNum "Past Month"
                    // ine so that it accepts any number of digits, mabye check if token[tokenIndex+1] can be converted to INT or if it is    in ['day','month','year']
                    if (tokens[tokenIndex + 1].Length < 3)
                    {
                        timeNum = tokens[tokenIndex + 1];
                        timeType = tokens[tokenIndex + 2];
                    }
                    else
                    {
                        timeType = tokens[tokenIndex + 1];
                    }
                    continue;
                }

                // Check for aggregates
                for (int i = 0; i < aggregates.Length; i++)
                {
                    if (aggregates[i] == token || aggAlternatives[i] == token)
                    {
                        aggregateFunction = aggregates[i];
                        break;
                    }
                }

                LookupValues lookupValues = _context.lookupvalues.Where(row => row.Value == token).FirstOrDefault();
                if (lookupValues != null)
                {
                    // Find Measurables
                    if (lookupValues.Type == "Measure")
                    {
                        listMeasures.Add(lookupValues.WhereStmt);
                    }
                    // Find WhereStmts
                    else if (lookupValues.Type == "Date")
                    {
                        listDates.Add(lookupValues.WhereStmt);
                    }
                    else
                    {
                        string statement = lookupValues.WhereStmt + " = " + "'" + lookupValues.Value + "'";
                        listWhereStatements.Add(statement);
                    }
                }
                tokenIndex++;
            }
        }


        /// <summary>
        /// Construct the query by adding the known filters
        /// </summary>
        /// <returns>query</returns>
        private string ConstructQuery()
        {
            string query = "";
            string measures = "";
            string wheres = "";

            if (listMeasures.Any())
            {
                measures = listMeasures.Aggregate((i, j) => i + ", " + j);

                // Add aggregate function
                if (!String.IsNullOrEmpty(aggregateFunction))
                {
                    measures = measures.Insert(0, aggregateFunction.ToUpper() + "(");
                    measures += ")";
                }
            }

            // If past,last keyword is detected
            if (!String.IsNullOrEmpty(timeType))
            {
                // Display last months or days of current year
                if ((timeType == "month" || timeType == "day") && !listDates.Any())
                {
                    wheres += timeType + "(PERIOD_START) between (" + timeType + "(getdate()) - " + timeNum + ") and getdate() and YEAR(PERIOD_START) = YEAR(getdate())";
                }
                // Display last months or days of specific year, e.g last 2 months of 2016
                else
                {
                    var year = listDates[0].Split('=')[1].Replace(" ", "");

                    if (timeType == "month")
                    {
                        var monthDiff = 12 - Int32.Parse(timeNum);
                        string month = monthDiff.ToString();
                        if (monthDiff >= 1 && monthDiff <= 9)
                        {
                            month = "0" + month;
                        }
                        wheres += "CONVERT(VARCHAR(10), PERIOD_START) between '" + year + "-" + month + "-01' and '" + year + "-12-31'";
                    }
                    else if (timeType == "day")
                    {
                        var dayDiff = 31 - Int32.Parse(timeNum);
                        string day = dayDiff.ToString();
                        if (dayDiff >= 1 && dayDiff <= 9)
                        {
                            day = "0" + day;
                        }
                        wheres += "CONVERT(VARCHAR(10), PERIOD_START) between '" + year + "-12-" + dayDiff + "' and '" + year + "-12-31'";
                    }
                }
            }

            // If dates are BETWEEN a certain range
            else if (listDates.Any() && isBetween)
            {
                wheres += listDates.Aggregate((i, j) => i.Split('=')[0] + "Between" + i.Split('=')[1] + " AND" + j.Split('=')[1]);
            }
            else if (listDates.Any())
            {
                if (!String.IsNullOrEmpty(wheres))
                    wheres += " AND ";
                wheres += listDates.Aggregate((i, j) => "(" + i + " OR " + j + ")");

            }


            // Add the rest of the "where" Statements
            if (listWhereStatements.Any())
            {
                if (!String.IsNullOrEmpty(wheres))
                    wheres += " AND ";
                wheres += listWhereStatements.Aggregate((i, j) => i + " AND " + j);
            }
            // Construct Query
            query = "SELECT * FROM TDATA";

            if (!String.IsNullOrEmpty(wheres))
                query += " WHERE " + wheres;

            return query;
        }
    }
}
