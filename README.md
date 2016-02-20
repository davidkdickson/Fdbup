# Fdbup

Fdbup is a library that you can use for deploying and upgrading SQL databases. It tracks which scripts have been run already, running only those change scripts that are needed to get your database up to date.

It is based on the [dbup project](http://dbup.github.io/). I used Fdbup as a way to improve my knowledge in functional programming and F#.

## Getting Started
1. Download the library
2. Create a console application with necessary setting (see: Fdbup.Console for the necessary settings)
3. Add the sql scripts to be managed as embedded resources

You now have a console application that can run change scripts.
