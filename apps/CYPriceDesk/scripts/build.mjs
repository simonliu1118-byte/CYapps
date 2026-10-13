import {mkdir,cp} from 'node:fs/promises';
await mkdir('dist',{recursive:true});
await cp('public','dist',{recursive:true});
console.log('Static Site built in dist/');
