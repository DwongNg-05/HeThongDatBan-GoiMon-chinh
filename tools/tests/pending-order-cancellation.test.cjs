const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('src/RestaurantManagement.Web/wwwroot/js/pending-order-cancellation.js','utf8');
const flush=()=>new Promise(r=>setImmediate(r));
const reply=(data,ok=true)=>({ok,json:async()=>data});
const line=(status='Pending',subtotal=75000)=>({id:1,sessionId:1,tableCode:'A01',itemName:'Cơm',unit:'phần',quantity:3,unitPrice:25000,status,statusLabel:status,subtotal});
function setup(fetch){
 const selectors={},timers=[];
 function el(tag){return {tag,children:[],dataset:{},listeners:{},disabled:false,value:'',append(...v){this.children.push(...v)},replaceChildren(){this.children=[]},addEventListener(k,v){this.listeners[k]=v},showModal(){this.open=true},close(){this.open=false;this.listeners.close?.()},querySelector(k){return selectors[k]??=el()},checkValidity(){return !!this.elements.reason.value},reportValidity(){return this.checkValidity()},reset(){this.elements.reason.value=''}}}
 const root=el(),form=root.querySelector('[data-cancel-form]');
 form.elements={quantity:el(),reason:el()};
 vm.runInNewContext(source,{document:{querySelector:()=>root,createElement:el},fetch,Intl,Map,crypto:{randomUUID:()=> 'request-1'},AbortSignal:{timeout:()=>undefined},FormData:class{constructor(f){this.data=new Map(Object.entries(f.elements).map(([k,v])=>[k,v.value]))}set(k,v){this.data.set(k,v)}get(k){return this.data.get(k)}},setInterval(f,ms){timers.push({f,ms})}});
 return {selectors,form,timers};
}
const all=e=>[e,...e.children.flatMap(all)];
test('mandatory reason, full quantity, disabled preparing and duplicate clicks',async()=>{
 let posts=0,finish;
 const s=setup(async(url,o)=>{if(o.method==='POST'){posts++;assert.equal(o.body.get('quantity'),3);return new Promise(r=>finish=r)}return reply([line(),{...line('Preparing'),id:2}])});
 await flush();
 const buttons=all(s.selectors['[data-order-lines]']).filter(e=>e.tag==='button');
 assert.equal(buttons[0].disabled,false);assert.equal(buttons[1].disabled,true);
 buttons[0].listeners.click();assert.equal(s.selectors['[data-cancel-submit]'].disabled,true);
 await s.form.listeners.submit({preventDefault(){}});assert.equal(posts,0);
 s.form.elements.reason.value='Mistake';
 const pending=s.form.listeners.submit({preventDefault(){}});
 await s.form.listeners.submit({preventDefault(){}});assert.equal(posts,1);
 finish(reply({message:'Đã huỷ'}));await pending;
 assert.equal(s.selectors.dialog.open,false);
});
test('kitchen conflict refreshes current state and blocks confirmation',async()=>{
 let state='Pending';
 const s=setup(async(url,o)=>{if(o.method==='POST'){state='Preparing';return reply({message:'Món không còn chờ bếp'},false)}return reply([line(state)])});
 await flush();all(s.selectors['[data-order-lines]']).find(e=>e.tag==='button').listeners.click();
 s.form.elements.reason.value='SoldOut';await s.form.listeners.submit({preventDefault(){}});
 assert.equal(s.selectors['[data-cancel-submit]'].disabled,true);
 assert.equal(s.form.elements.reason.disabled,true);
 assert.equal(all(s.selectors['[data-order-lines]']).find(e=>e.tag==='button').disabled,true);
 assert.equal(s.selectors['[data-cancel-message]'].textContent,'Món không còn chờ bếp');
});
test('cancelled subtotal updates, network retry reuses request id',async()=>{
 let state='Pending',posts=0;const ids=[];
 const s=setup(async(url,o)=>{if(o.method==='POST'){ids.push(o.body.get('requestId'));if(++posts===1)throw Error('network');state='Cancelled';return reply({message:'Đã huỷ'})}return reply([line(state,state==='Cancelled'?0:75000)])});
 await flush();all(s.selectors['[data-order-lines]']).find(e=>e.tag==='button').listeners.click();
 s.form.elements.reason.value='ChangedMind';
 await s.form.listeners.submit({preventDefault(){}});await s.form.listeners.submit({preventDefault(){}});
 assert.deepEqual(ids,['request-1','request-1']);
 assert.ok(all(s.selectors['[data-order-lines]']).some(e=>/^Tạm tính: 0\s₫$/.test(e.textContent)));
 assert.equal(s.timers[0].ms,2000);
});
test('kitchen auto refresh replaces queue after cancellation',async()=>{
 const timers=[];let replaced=false;
 const root={before(){},replaceWith(){replaced=true}};
 vm.runInNewContext(fs.readFileSync('src/RestaurantManagement.Web/wwwroot/js/kitchen-order-poll.js','utf8'),{
 document:{querySelector:()=>root,addEventListener(){},createElement:()=>({setAttribute(){}})},
 fetch:async()=>({ok:true,redirected:false,text:async()=>'<queue/>'}),
 DOMParser:class{parseFromString(){return {querySelector:()=>({})}}},AbortSignal:{timeout(){}},
 setInterval(f,ms){timers.push({f,ms})}});
 assert.equal(timers[0].ms,2000);await timers[0].f();assert.equal(replaced,true);
});
